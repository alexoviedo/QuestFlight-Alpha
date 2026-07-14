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
        private const int RequiredShotCount = 16;
        private const int DrawCallTarget = 180;
        private const long VisibleTriangleTarget = 700000L;
        private const int SceneMaterialTarget = 40;
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
                context.environment.airportContext, "AirportArtProduction");
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
                "AirportArtProduction");

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

            SetAircraftPose(context, context.originalAircraftPosition, context.originalAircraftRotation);
            context.animator?.ClearControlStateForTest();
            context.visibility?.SetCockpitView();
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
            if (shot.profile == ViewProfile.Cockpit) context.visibility?.SetCockpitView();
            else context.visibility?.SetExternalView();
            Renderer[] environmentHiddenRenderers = Array.Empty<Renderer>();
            bool[] environmentHiddenStates = Array.Empty<bool>();
            if (shot.profile == ViewProfile.Environment)
            {
                environmentHiddenRenderers = context.rig.AircraftVisualRoot.GetComponentsInChildren<Renderer>(true);
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
            string path = Path.Combine(outputDir, "screenshots", shot.id + ".png");
            Texture2D texture = Render(context.camera, width, height, out EditorFrameRenderStats editorStats);
            try
            {
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
                result.passed = result.imageNotBlank &&
                                (shot.category != "cockpit" || image.outsideViewRatio >= context.marker.PilotSeatProfile.minimumOutsideViewRatio) &&
                                budget.estimatedInstancedDrawCalls <= DrawCallTarget &&
                                budget.estimatedFrustumTriangles <= VisibleTriangleTarget;
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
                Object.DestroyImmediate(texture);
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
            const int columns = 4;
            const int cellWidth = 320;
            const int cellHeight = 180;
            int rows = Mathf.CeilToInt(report.shots.Count / (float)columns);
            Texture2D sheet = new Texture2D(columns * cellWidth, rows * cellHeight, TextureFormat.RGBA32, false);
            Color32[] background = Enumerable.Repeat(new Color32(16, 18, 22, 255), sheet.width * sheet.height).ToArray();
            sheet.SetPixels32(background);
            try
            {
                for (int index = 0; index < report.shots.Count; index++)
                {
                    string screenshotPath = report.shots[index].screenshotPath;
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
                string path = Path.Combine(outputDir, "visual_qa_contact_sheet.png");
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
