using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QuestFlight.Product;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;
using Object = UnityEngine.Object;

namespace QuestFlightLab.Runtime
{
    /// <summary>
    /// Canonical names and deterministic capture offsets for Meta XR Simulator QA.
    /// VRS files remain the authority for tracked-head and controller motion; this
    /// catalog never drives a camera or XR transform.
    /// </summary>
    public static class MetaXrSimulatorQaSessionCatalog
    {
        [Serializable]
        public sealed class Definition
        {
            public string id;
            public string description;
            public float captureDelaySeconds;
            public bool startAuthoredDemoPilot;

            public Definition(string sessionId, string sessionDescription, float delay, bool startDemo)
            {
                id = sessionId;
                description = sessionDescription;
                captureDelaySeconds = delay;
                startAuthoredDemoPilot = startDemo;
            }
        }

        private static readonly Definition[] Definitions =
        {
            new Definition("cockpit_neutral", "Cockpit neutral forward view", 4f, false),
            new Definition("head_pan", "Slow head pan left, right, up, and down", 8f, false),
            new Definition("rear_view", "180-degree rearward view", 6f, false),
            new Definition("viewpoint_calibration", "Viewpoint menu open, adjust, save, and reset", 10f, false),
            new Definition("control_surface_sweep", "Touch/gamepad control-surface sweep", 8f, false),
            new Definition("taxi", "Deterministic taxi phase", 7f, true),
            new Definition("takeoff_roll", "Deterministic takeoff roll", 20f, true),
            new Definition("rotation_climb", "Deterministic rotation and climb", 34f, true),
            new Definition("shallow_turns", "Recorded shallow left and right turns", 55f, false),
            new Definition("final_approach", "Recorded final approach inputs", 120f, false)
        };

        public static IReadOnlyList<Definition> All => Definitions;

        public static bool TryGet(string id, out Definition definition)
        {
            definition = Definitions.FirstOrDefault(item =>
                string.Equals(item.id, id, StringComparison.OrdinalIgnoreCase));
            return definition != null;
        }
    }

    /// <summary>
    /// Read-only architecture checks shared by the development-player witness and Editor tests.
    /// Presentation interpolation intentionally gives AircraftVisualRoot a transient local pose;
    /// pivot ownership is established by the authored hierarchy and zero CG reference instead.
    /// </summary>
    public static class MetaXrSimulatorQaArchitectureChecks
    {
        public static bool UsesAuthoredCenterOfGravityPivot(AircraftReferenceFrameRig rig)
        {
            if (rig == null || rig.AircraftSimulationRoot == null ||
                rig.CenterOfGravityReference == null || rig.AircraftVisualRoot == null ||
                rig.TrackedCamera == null)
            {
                return false;
            }

            Transform simulationRoot = rig.AircraftSimulationRoot;
            bool cgAtRoot = rig.CenterOfGravityReference.parent == simulationRoot &&
                            rig.CenterOfGravityReference.localPosition.sqrMagnitude < 0.000001f;
            bool visualOwnedBySimulationRoot = rig.AircraftVisualRoot.parent == simulationRoot &&
                                               (rig.AircraftVisualRoot.localScale - Vector3.one).sqrMagnitude < 0.000001f;
            bool cameraNotAncestor = !simulationRoot.IsChildOf(rig.TrackedCamera.transform);
            return rig.ValidateHierarchy() && cgAtRoot && visualOwnedBySimulationRoot && cameraNotAncestor;
        }
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    /// <summary>
    /// Development-player-only Meta XR Simulator witness. The component is
    /// created only when QFL_META_XR_SIMULATOR_QA_DIR is present. It constructs
    /// no product XR, aircraft, environment, or backend object and never writes
    /// the OpenXR-owned Main Camera.
    /// </summary>
    internal sealed class MetaXrSimulatorQaRuntime : MonoBehaviour
    {
        private const int DefaultWidth = 1440;
        private const int DefaultHeight = 1440;
        private const float StartupTimeoutSeconds = 45f;
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _errors = new List<string>();
        private MountainTemporalStabilityProbe _mountainProbe;
        private ProductDemoPilot _demoPilot;
        private string _outputDirectory;
        private string _sessionId;
        private bool _finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            string output = System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_QA_DIR");
            if (string.IsNullOrWhiteSpace(output)) return;

            GameObject witness = new GameObject("__MetaXrSimulatorQaWitness")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(witness);
            witness.AddComponent<MetaXrSimulatorQaRuntime>();
        }

        private void Awake()
        {
            _outputDirectory = System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_QA_DIR");
            _sessionId = System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_SESSION") ?? "unknown";
            Directory.CreateDirectory(_outputDirectory);
            Application.runInBackground = true;
            Application.targetFrameRate = 72;
            Application.logMessageReceived += OnLogMessage;
        }

        private IEnumerator Start()
        {
            MetaXrSimulatorQaSessionCatalog.TryGet(_sessionId, out MetaXrSimulatorQaSessionCatalog.Definition definition);
            float started = Time.realtimeSinceStartup;
            ProductionVerticalSliceRoot productRoot = null;
            AircraftReferenceFrameRig rig = null;
            while (Time.realtimeSinceStartup - started < StartupTimeoutSeconds)
            {
                productRoot = Object.FindFirstObjectByType<ProductionVerticalSliceRoot>(FindObjectsInactive.Exclude);
                rig = productRoot != null ? productRoot.AircraftRig : null;
                if (productRoot != null && rig != null && rig.TrackedCamera != null &&
                    XRSettings.isDeviceActive && rig.TrackedCamera.stereoEnabled)
                {
                    break;
                }
                yield return null;
            }

            if (productRoot == null || rig == null || rig.TrackedCamera == null)
            {
                _errors.Add("Authored ProductionVerticalSlice root, aircraft rig, or tracked camera was not available.");
                WriteStartupFailure(productRoot, rig);
                yield break;
            }

            _mountainProbe = Object.FindFirstObjectByType<MountainTemporalStabilityProbe>(FindObjectsInactive.Exclude);
            if (_mountainProbe != null)
            {
                if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_TRAINING_VIEW")))
                    _mountainProbe.BeginCapture(rig.TrackedCamera);
            }
            else _errors.Add("Authored MountainTemporalStabilityProbe was not available.");

            var atmosphereAuthority=rig.AircraftSimulationRoot.GetComponent<FlightDynamicsCoordinator>();
            float nativeStarted=Time.realtimeSinceStartup;
            while(atmosphereAuthority!=null && !atmosphereAuthority.IsInitialized && Time.realtimeSinceStartup-nativeStarted<StartupTimeoutSeconds)
                yield return null;
            if(atmosphereAuthority==null || !atmosphereAuthority.IsInitialized)
            { _errors.Add("Native flight authority not ready for simulator scenario.");WriteStartupFailure(productRoot,rig);yield break; }
            _demoPilot = Object.FindFirstObjectByType<ProductDemoPilot>(FindObjectsInactive.Exclude);
            bool startDemo = ReadBool("QFL_META_XR_SIMULATOR_START_DEMO", definition != null && definition.startAuthoredDemoPilot);
            if (startDemo)
            {
                var modes=Object.FindFirstObjectByType<ProductFlightModeCoordinator>();
                if(modes!=null) modes.SelectDemo();
                else if (_demoPilot != null) _demoPilot.StartDemo();
                else _errors.Add("The authored ProductDemoPilot was unavailable for the requested flight phase.");
            }
            else if (_demoPilot != null && _demoPilot.IsRunning)
            {
                _demoPilot.StopDemo();
            }
            string trainingView = System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_TRAINING_VIEW");
            if (!startDemo && !string.IsNullOrEmpty(trainingView))
            {
                var mode = Object.FindFirstObjectByType<ProductFlightModeCoordinator>();
                mode.SelectTraining();
                float readyAt = Time.realtimeSinceStartup;
                while (mode.AuthoredTraining.State != QuestFlight.Product.Training.TrainingSessionState.Menu && Time.realtimeSinceStartup - readyAt < 15)
                    yield return null;
                if (mode.AuthoredTraining.State != QuestFlight.Product.Training.TrainingSessionState.Menu)
                { _errors.Add("Training menu did not finish its authored transition."); WriteStartupFailure(productRoot, rig); yield break; }
                // The showroom deliberately hides terrain. Begin the stability witness
                // after the normal transition restores it, not across that visibility switch.
                if (_mountainProbe != null)
                {
                    _mountainProbe.Initialize(_mountainProbe.transform, _mountainProbe.transform.parent);
                    _mountainProbe.BeginCapture(rig.TrackedCamera);
                }
                if (trainingView != "menu") mode.AuthoredTraining.SelectLesson(1);
                if (trainingView == "practice") mode.AuthoredTraining.StartPractice();
                Debug.Log($"[TrainingQA] requested={trainingView} state={mode.AuthoredTraining.State} focused={Application.isFocused}");
            }
            else if(!startDemo) Object.FindFirstObjectByType<ProductFlightModeCoordinator>()?.SelectFreeFlight();

            // Development-only scenario placement goes through native reset; tracked eye poses stay OpenXR-owned.
            if(System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_AMBIENT_VIEW")=="foothill")
            {
                yield return null;yield return null;
                var traffic=Object.FindFirstObjectByType<AmbientTrafficDirector>();
                var glider=traffic!=null?traffic.actors[4]:null;
                if(glider==null){_errors.Add("Missing authored foothill traffic.");WriteStartupFailure(productRoot,rig);yield break;}
                Vector3 at=glider.Position+new Vector3(500,15,-300);
                var geo=FlightFrameConversions.UnityToGeodetic(at,GeodeticReference.Kbdu);
                Vector3 direction=glider.Position-at;
                var initial=atmosphereAuthority.LastResetInitialConditions;
                initial.latitudeDegrees=geo.latitudeDegrees;initial.longitudeDegrees=geo.longitudeDegrees;initial.altitudeMslMeters=geo.altitudeMslMeters;
                initial.calibratedAirspeedKnots=80;initial.headingDegrees=Mathf.Repeat(Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg,360);
                initial.pitchDegrees=0;initial.bankDegrees=0;initial.flightPathAngleDegrees=0;
                if(!atmosphereAuthority.TryRecoverToInitialConditions(initial))
                {_errors.Add(atmosphereAuthority.LastError);WriteStartupFailure(productRoot,rig);yield break;}
            }

            // Bounded road-repair inspection in the Windows QA player only.
            // Place the aircraft through native JSBSim; never move a tracked eye.
            if (System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_ROAD_VIEW") == "airport")
            {
                yield return null;
                var geo = FlightFrameConversions.UnityToGeodetic(new Vector3(-1100, 120, -650), GeodeticReference.Kbdu);
                var initial = atmosphereAuthority.LastResetInitialConditions;
                initial.latitudeDegrees = geo.latitudeDegrees;
                initial.longitudeDegrees = geo.longitudeDegrees;
                initial.altitudeMslMeters = geo.altitudeMslMeters;
                initial.calibratedAirspeedKnots = 80;
                initial.headingDegrees = 300;
                initial.pitchDegrees = 0;
                initial.bankDegrees = 0;
                initial.flightPathAngleDegrees = 0;
                if (!atmosphereAuthority.TryRecoverToInitialConditions(initial))
                {
                    _errors.Add(atmosphereAuthority.LastError);
                    WriteStartupFailure(productRoot, rig);
                    yield break;
                }
            }

            float delay = ReadFloat(
                "QFL_META_XR_SIMULATOR_CAPTURE_DELAY_SECONDS",
                definition != null ? definition.captureDelaySeconds : 5f);
            float delayStarted = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - delayStarted < Mathf.Max(0.5f, delay))
                yield return null;
            if (!string.IsNullOrEmpty(trainingView))
            {
                var training = Object.FindFirstObjectByType<QuestFlight.Product.Training.ProductTrainingController>();
                string expected = trainingView == "practice" ? "Practice" : trainingView == "menu" ? "Menu" : "Briefing";
                if (training.State.ToString() != expected) _errors.Add($"Training capture expected {expected}, found {training.State}; focused={Application.isFocused}.");
                Debug.Log($"[TrainingQA] capture state={training.State} focused={Application.isFocused}");
            }

            int temporalFrames=Mathf.Clamp(ReadInt("QFL_META_XR_SIMULATOR_TEMPORAL_FRAMES",0),0,30);
            bool ambientWindows=ReadBool("QFL_META_XR_SIMULATOR_AMBIENT_WINDOWS",false);
            for(int frame=0;frame<temporalFrames;frame++)
            {
                yield return new WaitForEndOfFrame();
                Camera tracked=rig.TrackedCamera;
                for(int eye=0;eye<2;eye++)
                {
                    var side=(Camera.StereoscopicEye)eye;
                    var texture=RenderEye(tracked,tracked.GetStereoViewMatrix(side),tracked.GetStereoProjectionMatrix(side),1440,1440);
                    File.WriteAllBytes(Path.Combine(_outputDirectory,$"temporal_{frame:00}_{side}.png"),texture.EncodeToPNG());
                    Object.Destroy(texture);
                }
                if(ambientWindows)
                {
                    // Separate QA eye cameras simulate seated window views. Never
                    // rotate or write the OpenXR-owned tracked camera or XR rig.
                    foreach(float yaw in new[]{-45f,75f})
                    {
                        var center=tracked.transform.position;
                        var turn=Matrix4x4.Translate(center)*Matrix4x4.Rotate(Quaternion.AngleAxis(yaw,Vector3.up))*Matrix4x4.Translate(-center);
                        for(int eye=0;eye<2;eye++)
                        {
                            var side=(Camera.StereoscopicEye)eye;
                            var view=tracked.GetStereoViewMatrix(side)*turn.inverse;
                            var texture=RenderEye(tracked,view,tracked.GetStereoProjectionMatrix(side),1440,1440);
                            File.WriteAllBytes(Path.Combine(_outputDirectory,$"window_{yaw}_{frame:00}_{side}.png"),texture.EncodeToPNG());
                            Object.Destroy(texture);
                        }
                    }
                }
                WriteAmbientWitness(rig,$"ambient_{frame:00}.json");
                yield return new WaitForSeconds(1);
            }
            yield return new WaitForEndOfFrame();
            Capture(productRoot, rig, definition);
        }

        [Serializable] private class AmbientWitness {public AmbientWitnessRow[] actors;}
        [Serializable] private class AmbientWitnessRow {public string id,phase;public Vector3 position,leftViewport,rightViewport;public float distance;public bool suppressed;public int activeRenderers,lod,runwayOwner,groundOwner;public Vector3 velocity;public string blocked;}

        private void WriteAmbientWitness(AircraftReferenceFrameRig rig,string filename)
        {
            var trafficWitness=Object.FindFirstObjectByType<AmbientTrafficDirector>();
            if(trafficWitness!=null)
            {
                var rows=new AmbientWitnessRow[trafficWitness.actors.Length];
                for(int i=0;i<rows.Length;i++)
                {
                    var a=trafficWitness.actors[i];
                    rows[i]=new AmbientWitnessRow{id=a.aircraftId,phase=a.sailplane?a.SoaringPhase.ToString():a.Phase.ToString(),position=a.Position,
                        distance=Vector3.Distance(a.Position,rig.TrackedCamera.transform.position),suppressed=a.Suppressed,
                        lod=a.SelectedLod,velocity=a.Velocity,blocked=a.BlockingReason,runwayOwner=trafficWitness.RunwayOwner,groundOwner=trafficWitness.GroundMovementOwner,
                        leftViewport=rig.TrackedCamera.WorldToViewportPoint(a.Position,Camera.MonoOrStereoscopicEye.Left),
                        rightViewport=rig.TrackedCamera.WorldToViewportPoint(a.Position,Camera.MonoOrStereoscopicEye.Right),
                        activeRenderers=a.renderers.Count(r=>r.enabled&&r.gameObject.activeInHierarchy)};
                }
                File.WriteAllText(Path.Combine(_outputDirectory,filename),JsonUtility.ToJson(new AmbientWitness{actors=rows},true));
            }
        }

        private void Capture(
            ProductionVerticalSliceRoot productRoot,
            AircraftReferenceFrameRig rig,
            MetaXrSimulatorQaSessionCatalog.Definition definition)
        {
            if (_finished) return;
            _finished = true;

            int width = Mathf.Clamp(ReadInt("QFL_META_XR_SIMULATOR_CAPTURE_WIDTH", DefaultWidth), 256, 4096);
            int height = Mathf.Clamp(ReadInt("QFL_META_XR_SIMULATOR_CAPTURE_HEIGHT", DefaultHeight), 256, 4096);
            string stem = SanitizeFileName(_sessionId);
            string leftPath = Path.Combine(_outputDirectory, stem + "_left.png");
            string rightPath = Path.Combine(_outputDirectory, stem + "_right.png");
            string stereoPath = Path.Combine(_outputDirectory, stem + "_stereo.png");

            MetaXrSimulatorSessionReport report = CreateBaseReport(productRoot, rig, definition, width, height);
            Texture2D left = null;
            Texture2D right = null;
            try
            {
                Camera tracked = rig.TrackedCamera;
                Matrix4x4 leftView = tracked.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
                Matrix4x4 rightView = tracked.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
                Matrix4x4 leftProjection = tracked.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
                Matrix4x4 rightProjection = tracked.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);
                report.leftEye = EyeMetadata.FromMatrices(leftView, leftProjection);
                report.rightEye = EyeMetadata.FromMatrices(rightView, rightProjection);
                report.eyeSeparationMeters = Vector3.Distance(report.leftEye.worldPosition, report.rightEye.worldPosition);

                left = RenderEye(tracked, leftView, leftProjection, width, height);
                right = RenderEye(tracked, rightView, rightProjection, width, height);
                File.WriteAllBytes(leftPath, left.EncodeToPNG());
                File.WriteAllBytes(rightPath, right.EncodeToPNG());
                Texture2D stereo = BuildStereo(left, right);
                File.WriteAllBytes(stereoPath, stereo.EncodeToPNG());
                Object.Destroy(stereo);

                report.leftEyeImage = leftPath;
                report.rightEyeImage = rightPath;
                report.stereoImage = stereoPath;
                report.leftImageMetrics = ImageMetrics.Measure(left);
                report.rightImageMetrics = ImageMetrics.Measure(right);
                report.meanAbsoluteStereoDifference = MeanAbsoluteDifference(left, right);
            }
            catch (Exception exception)
            {
                _errors.Add("Stereo capture failed: " + exception);
            }
            finally
            {
                if (left != null) Object.Destroy(left);
                if (right != null) Object.Destroy(right);
            }

            WriteAmbientWitness(rig,"ambient_witness.json");
            PopulateArchitectureAndEnvironmentChecks(report, productRoot, rig);
            PopulateMountainCheck(report);
            PopulateUiAndDebugChecks(report, productRoot, rig);
            PopulateRuntimeChecks(report, rig);
            report.warnings = _warnings.Distinct().ToList();
            report.errors = _errors.Distinct().ToList();
            report.checks.noMissingReferencesOrComponents =
                report.missingComponentCount == 0 &&
                !report.errors.Any(IsMissingReferenceMessage);
            report.checks.bothEyesNonBlank =
                report.leftImageMetrics != null && report.rightImageMetrics != null &&
                !report.leftImageMetrics.blank && !report.rightImageMetrics.blank;
            report.checks.stereoSceneConsistency =
                report.checks.bothEyesNonBlank &&
                report.meanAbsoluteStereoDifference >= 0.0001f &&
                report.meanAbsoluteStereoDifference <= 0.35f;
            report.passed = report.checks.AllPassed() && report.errors.Count == 0;
            report.classification = report.passed
                ? "PASS_META_XR_SIMULATOR_MACHINE_GATE_HUMAN_IMAGE_REVIEW_REQUIRED"
                : "FAIL_META_XR_SIMULATOR_MACHINE_GATE";

            string jsonPath = Path.Combine(_outputDirectory, stem + "_session_report.json");
            File.WriteAllText(jsonPath, JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(_outputDirectory, stem + "_session_summary.md"), BuildMarkdown(report));
            Debug.Log($"[QuestFlight][MetaXRSimulatorQA] {report.classification} session={_sessionId} report={jsonPath}");
            CompleteAndExit(report.passed ? 0 : 3);
        }

        private MetaXrSimulatorSessionReport CreateBaseReport(
            ProductionVerticalSliceRoot productRoot,
            AircraftReferenceFrameRig rig,
            MetaXrSimulatorQaSessionCatalog.Definition definition,
            int width,
            int height)
        {
            XRDisplaySubsystem display = ResolveDisplaySubsystem();
            float refreshRate = 0f;
            bool refreshAvailable = display != null && display.TryGetDisplayRefreshRate(out refreshRate);
            return new MetaXrSimulatorSessionReport
            {
                schemaVersion = 1,
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                sessionId = _sessionId,
                sessionDescription = definition != null ? definition.description : "Unknown session",
                replayPath = System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_REPLAY_PATH") ?? string.Empty,
                sceneName = SceneManager.GetActiveScene().name,
                architectureVersion = productRoot.AuthoredArchitectureVersion,
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                openXrRuntimeName = SafeOpenXrRuntimeValue(() => OpenXRRuntime.name),
                openXrRuntimeVersion = SafeOpenXrRuntimeValue(() => OpenXRRuntime.version),
                openXrApiVersion = SafeOpenXrRuntimeValue(() => OpenXRRuntime.apiVersion),
                xrLoadedDeviceName = XRSettings.loadedDeviceName,
                xrDeviceActive = XRSettings.isDeviceActive,
                stereoEnabled = rig.TrackedCamera.stereoEnabled,
                stereoRenderingMode = XRSettings.stereoRenderingMode.ToString(),
                requestedRefreshRateHz = 72f,
                displayRefreshRateAvailable = refreshAvailable,
                measuredSimulatorDisplayRefreshRateHz = refreshRate,
                frameNumber = Time.frameCount,
                unscaledTimestampSeconds = Time.unscaledTime,
                captureWidth = width,
                captureHeight = height,
                qualityLevel = QualitySettings.names[QualitySettings.GetQualityLevel()],
                activeEnvironmentProfile = ResolveEnvironmentProfile(),
                activeRenderProfile = $"{SystemInfo.graphicsDeviceType}; stereo={XRSettings.stereoRenderingMode}; msaa={QualitySettings.antiAliasing}",
                trackedCameraPose = PoseMetadata.FromTransform(rig.TrackedCamera.transform),
                xrOriginPose = PoseMetadata.FromTransform(rig.XrOrigin),
                aircraftPose = PoseMetadata.FromTransform(rig.AircraftSimulationRoot),
                demoPilotPhase = _demoPilot != null ? _demoPilot.Phase : "not_running"
            };
        }

        private void PopulateArchitectureAndEnvironmentChecks(
            MetaXrSimulatorSessionReport report,
            ProductionVerticalSliceRoot productRoot,
            AircraftReferenceFrameRig rig)
        {
            ProductionVerticalSliceRoot[] productRoots = Object.FindObjectsByType<ProductionVerticalSliceRoot>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            AircraftReferenceFrameRig[] rigs = Object.FindObjectsByType<AircraftReferenceFrameRig>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Camera[] trackedMainCameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(camera => camera != null && camera.CompareTag("MainCamera") && camera.enabled)
                .ToArray();
            report.productRootCount = productRoots.Length;
            report.aircraftRigCount = rigs.Length;
            report.enabledTrackedMainCameraCount = trackedMainCameras.Length;
            report.missingComponentCount = CountMissingComponents(SceneManager.GetActiveScene());
            report.renderRootSignatureSha256 = BuildRenderSignature(SceneManager.GetActiveScene(), false);
            report.materialSignatureSha256 = BuildRenderSignature(SceneManager.GetActiveScene(), true);

            report.checks.singleAuthoredProductHierarchy =
                productRoots.Length == 1 && rigs.Length == 1 && trackedMainCameras.Length == 1 &&
                ReferenceEquals(productRoots[0], productRoot) && ReferenceEquals(rigs[0], rig);
            report.checks.aircraftUsesCenterOfGravityPivot =
                MetaXrSimulatorQaArchitectureChecks.UsesAuthoredCenterOfGravityPivot(rig);

            ProductionEnvironmentRoot environment = Object.FindFirstObjectByType<ProductionEnvironmentRoot>(FindObjectsInactive.Include);
            string contract = "ProductionEnvironmentRoot missing";
            report.environmentContractPassed = environment != null && environment.TryValidateContract(out contract);
            report.environmentContract = contract;
            report.checks.runwayAircraftTerrainAttached =
                report.environmentContractPassed &&
                environment != null &&
                environment.measuredRunwayToTerrainMaximumGapMeters <= 0.025f &&
                environment.measuredCollisionSurfaceDisagreementMeters <= 0.0001f;
        }

        private void PopulateMountainCheck(MetaXrSimulatorSessionReport report)
        {
            if (_mountainProbe == null)
            {
                report.checks.mountainIdentityImmutable = false;
                return;
            }
            report.mountainStability = MountainStabilityMetadata.FromReport(_mountainProbe.EndCapture(false));
            report.checks.mountainIdentityImmutable = report.mountainStability != null &&
                                                       report.mountainStability.immutableTransforms &&
                                                       report.mountainStability.immutableMeshes &&
                                                       report.mountainStability.immutableRendererSet &&
                                                       report.mountainStability.sampledFrameCount > 0;
        }

        private void PopulateUiAndDebugChecks(
            MetaXrSimulatorSessionReport report,
            ProductionVerticalSliceRoot productRoot,
            AircraftReferenceFrameRig rig)
        {
            ProductionSeatCalibrationController seat = productRoot.SeatCalibration;
            bool calibrationSession = string.Equals(_sessionId, "viewpoint_calibration", StringComparison.OrdinalIgnoreCase);
            report.viewpointUiOpenAtCapture = seat != null && seat.IsOpen;
            report.viewpointUiViewportRect = seat != null && seat.AuthoredPanelRoot != null && seat.AuthoredPanelRoot.activeInHierarchy
                ? ProjectRendererBounds(rig.TrackedCamera, seat.AuthoredPanelRoot.GetComponentsInChildren<Renderer>(true))
                : new Rect();

            // Conservative protected region for the primary instrument panel. This is a machine
            // precheck only; the full-resolution stereo pair remains the acceptance evidence.
            Rect primaryPanelProtectedRegion = new Rect(0.16f, 0.04f, 0.60f, 0.50f);
            float overlap = RectIntersectionArea(report.viewpointUiViewportRect, primaryPanelProtectedRegion);
            float uiArea = Mathf.Max(0.000001f,
                report.viewpointUiViewportRect.width * report.viewpointUiViewportRect.height);
            report.viewpointUiProtectedRegionOverlapRatio = overlap / uiArea;
            report.checks.viewpointUiDoesNotOverlapPrimaryPanel = !calibrationSession ||
                (report.viewpointUiOpenAtCapture && report.viewpointUiProtectedRegionOverlapRatio <= 0.10f);

            report.activeDebugUiObjects = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(transform => transform != null && IsDebugUiName(transform.name))
                .Select(HierarchyPath)
                .Distinct()
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
            report.checks.notDominatedByDebugUi = report.activeDebugUiObjects.Count == 0;
        }

        private void PopulateRuntimeChecks(MetaXrSimulatorSessionReport report, AircraftReferenceFrameRig rig)
        {
            report.hmdInputDevice = XrInputDeviceMetadata.FromDevice(InputDevices.GetDeviceAtXRNode(XRNode.Head));
            report.leftControllerInputDevice = XrInputDeviceMetadata.FromDevice(InputDevices.GetDeviceAtXRNode(XRNode.LeftHand));
            report.rightControllerInputDevice = XrInputDeviceMetadata.FromDevice(InputDevices.GetDeviceAtXRNode(XRNode.RightHand));
            // Simulator v201 identifies itself as Oculus, version 201.0.0.
            // Check the audited launch manifest and loaded version, not a guessed name.
            string manifest=System.Environment.GetEnvironmentVariable("XR_RUNTIME_JSON");
            bool expectedVersion=Version.TryParse(System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_EXPECTED_RUNTIME_VERSION"),out var expected) &&
                Version.TryParse(report.openXrRuntimeVersion,out var loaded) && expected.Major==loaded.Major && expected.Minor==loaded.Minor;
            report.checks.metaSimulatorRuntimeConnected = report.xrDeviceActive && report.stereoEnabled && expectedVersion &&
                !string.IsNullOrEmpty(manifest) && File.Exists(manifest) &&
                string.Equals(Path.GetFileName(manifest),"meta_openxr_simulator.json",StringComparison.OrdinalIgnoreCase);
            report.checks.quest3RefreshRate72Hz = report.displayRefreshRateAvailable &&
                                                      Mathf.Abs(report.measuredSimulatorDisplayRefreshRateHz - 72f) <= 0.5f;
            report.checks.stereoEyePosesPlausible = report.eyeSeparationMeters >= 0.03f &&
                                                   report.eyeSeparationMeters <= 0.09f;
            report.checks.trackedCameraOwnedByOpenXr =
                rig.TrackedCamera != null && rig.TrackedCamera.stereoEnabled && XRSettings.isDeviceActive;
            report.checks.simulatedHmdAndTouchControllersAvailable =
                report.hmdInputDevice.valid && report.leftControllerInputDevice.valid &&
                report.rightControllerInputDevice.valid;
        }

        private static Texture2D RenderEye(
            Camera tracked,
            Matrix4x4 view,
            Matrix4x4 projection,
            int width,
            int height)
        {
            GameObject cameraObject = new GameObject("__MetaXrSimulatorQaEyeCamera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.CopyFrom(tracked);
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.enabled = false;
            Matrix4x4 world = view.inverse;
            camera.transform.SetPositionAndRotation(
                world.GetColumn(3),
                Quaternion.LookRotation(-world.GetColumn(2), world.GetColumn(1)));
            camera.worldToCameraMatrix = view;
            camera.projectionMatrix = projection;

            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing),
                useMipMap = false,
                autoGenerateMips = false,
                name = "Meta XR Simulator QA Eye"
            };
            target.Create();
            RenderTexture previous = RenderTexture.active;
            var groves = Object.FindObjectsByType<MapBranchGroveRenderer>(FindObjectsSortMode.None);
            Camera.CameraCallback drawGroves = renderingCamera =>
            {
                if (renderingCamera != camera) return;
                foreach (var grove in groves) grove.DrawSimulatorEyeCapture(tracked, camera);
            };
            try
            {
                Camera.onPreCull += drawGroves;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                texture.Apply(false, false);
                return texture;
            }
            finally
            {
                Camera.onPreCull -= drawGroves;
                camera.targetTexture = null;
                RenderTexture.active = previous;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(cameraObject);
            }
        }

        private static Texture2D BuildStereo(Texture2D left, Texture2D right)
        {
            Texture2D stereo = new Texture2D(left.width * 2, left.height, TextureFormat.RGB24, false, false);
            stereo.SetPixels32(0, 0, left.width, left.height, left.GetPixels32());
            stereo.SetPixels32(left.width, 0, right.width, right.height, right.GetPixels32());
            stereo.Apply(false, false);
            return stereo;
        }

        private static float MeanAbsoluteDifference(Texture2D left, Texture2D right)
        {
            Color32[] a = left.GetPixels32();
            Color32[] b = right.GetPixels32();
            int stride = Mathf.Max(1, a.Length / 120000);
            double total = 0d;
            long count = 0;
            for (int i = 0; i < a.Length && i < b.Length; i += stride)
            {
                total += Math.Abs(a[i].r - b[i].r) / 255d;
                total += Math.Abs(a[i].g - b[i].g) / 255d;
                total += Math.Abs(a[i].b - b[i].b) / 255d;
                count += 3;
            }
            return count > 0 ? (float)(total / count) : 1f;
        }

        private static int CountMissingComponents(Scene scene)
        {
            int missing = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                Component[] components = transform.GetComponents<Component>();
                missing += components.Count(component => component == null);
            }
            return missing;
        }

        private static string BuildRenderSignature(Scene scene, bool materialsOnly)
        {
            List<string> entries = new List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (materialsOnly)
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        entries.Add(material != null
                            ? material.name + "|" + (material.shader != null ? material.shader.name : "null")
                            : "null");
                    }
                }
                else
                {
                    entries.Add(HierarchyPath(renderer.transform) + "|" + renderer.GetType().Name);
                }
            }
            entries.Sort(StringComparer.Ordinal);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", entries)));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static Rect ProjectRendererBounds(Camera camera, IEnumerable<Renderer> renderers)
        {
            bool any = false;
            float minX = 1f, minY = 1f, maxX = 0f, maxY = 0f;
            foreach (Renderer renderer in renderers.Where(item => item != null && item.enabled && item.gameObject.activeInHierarchy))
            {
                Bounds bounds = renderer.bounds;
                Vector3 extents = bounds.extents;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = bounds.center + Vector3.Scale(extents, new Vector3(x, y, z));
                    Vector3 viewport = camera.WorldToViewportPoint(point);
                    if (viewport.z <= 0f) continue;
                    any = true;
                    minX = Mathf.Min(minX, viewport.x);
                    minY = Mathf.Min(minY, viewport.y);
                    maxX = Mathf.Max(maxX, viewport.x);
                    maxY = Mathf.Max(maxY, viewport.y);
                }
            }
            return any
                ? Rect.MinMaxRect(Mathf.Clamp01(minX), Mathf.Clamp01(minY), Mathf.Clamp01(maxX), Mathf.Clamp01(maxY))
                : new Rect();
        }

        private static float RectIntersectionArea(Rect a, Rect b)
        {
            float minX = Mathf.Max(a.xMin, b.xMin);
            float minY = Mathf.Max(a.yMin, b.yMin);
            float maxX = Mathf.Min(a.xMax, b.xMax);
            float maxY = Mathf.Min(a.yMax, b.yMax);
            return Mathf.Max(0f, maxX - minX) * Mathf.Max(0f, maxY - minY);
        }

        private static bool IsDebugUiName(string name)
        {
            string normalized = name.ToLowerInvariant();
            return normalized.Contains("debugoverlay") || normalized.Contains("trainingoverlay") ||
                   normalized.Contains("devhud") || normalized.Contains("debugcanvas") ||
                   normalized.Contains("telemetryoverlay");
        }

        private static bool IsMissingReferenceMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            return message.IndexOf("MissingReference", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("MissingComponent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("missing script", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ResolveEnvironmentProfile()
        {
            ProductionEnvironmentRoot environment = Object.FindFirstObjectByType<ProductionEnvironmentRoot>(FindObjectsInactive.Include);
            return environment != null
                ? $"{environment.name}; macro={environment.uniqueMacroAlbedo?.name ?? "missing"}; far={environment.farTerrainMesh?.sharedMesh?.name ?? "missing"}"
                : "missing";
        }

        private static XRDisplaySubsystem ResolveDisplaySubsystem()
        {
            List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            return displays.FirstOrDefault(display => display != null && display.running);
        }

        private static string SafeOpenXrRuntimeValue(Func<string> getter)
        {
            try { return getter() ?? string.Empty; }
            catch (Exception exception) { return "unavailable: " + exception.GetType().Name; }
        }

        private static int ReadInt(string name, int fallback)
        {
            return int.TryParse(System.Environment.GetEnvironmentVariable(name), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        private static float ReadFloat(string name, float fallback)
        {
            return float.TryParse(System.Environment.GetEnvironmentVariable(name), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float value) ? value : fallback;
        }

        private static bool ReadBool(string name, bool fallback)
        {
            string value = System.Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            string message = condition + (string.IsNullOrWhiteSpace(stackTrace) ? string.Empty : "\n" + stackTrace);
            if (type == LogType.Warning) _warnings.Add(message);
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) _errors.Add(message);
        }

        private void WriteStartupFailure(ProductionVerticalSliceRoot productRoot, AircraftReferenceFrameRig rig)
        {
            MetaXrSimulatorSessionReport report = new MetaXrSimulatorSessionReport
            {
                schemaVersion = 1,
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                sessionId = _sessionId,
                sceneName = SceneManager.GetActiveScene().name,
                unityVersion = Application.unityVersion,
                xrLoadedDeviceName = XRSettings.loadedDeviceName,
                xrDeviceActive = XRSettings.isDeviceActive,
                stereoEnabled = rig != null && rig.TrackedCamera != null && rig.TrackedCamera.stereoEnabled,
                errors = _errors,
                warnings = _warnings,
                classification = "FAIL_META_XR_SIMULATOR_STARTUP",
                passed = false
            };
            string stem = SanitizeFileName(_sessionId);
            File.WriteAllText(Path.Combine(_outputDirectory, stem + "_session_report.json"), JsonUtility.ToJson(report, true));
            CompleteAndExit(4);
        }

        private void CompleteAndExit(int exitCode)
        {
            Application.logMessageReceived -= OnLogMessage;
            if (ReadBool("QFL_META_XR_SIMULATOR_EXIT_AFTER_CAPTURE", true))
                Application.Quit(exitCode);
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value;
        }

        private static string HierarchyPath(Transform transform)
        {
            Stack<string> names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names.ToArray());
        }

        private static string BuildMarkdown(MetaXrSimulatorSessionReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Meta XR Simulator session: " + report.sessionId);
            builder.AppendLine();
            builder.AppendLine("- Classification: **" + report.classification + "**");
            builder.AppendLine("- Runtime: " + report.openXrRuntimeName + " " + report.openXrRuntimeVersion);
            builder.AppendLine("- Stereo: " + report.stereoEnabled + "; eye separation " +
                               report.eyeSeparationMeters.ToString("0.0000", CultureInfo.InvariantCulture) + " m");
            builder.AppendLine("- Simulator display refresh: " +
                               (report.displayRefreshRateAvailable
                                   ? report.measuredSimulatorDisplayRefreshRateHz.ToString("0.0", CultureInfo.InvariantCulture) + " Hz"
                                   : "unavailable"));
            builder.AppendLine("- Left/right/stereo: `" + report.leftEyeImage + "`, `" + report.rightEyeImage + "`, `" + report.stereoImage + "`");
            builder.AppendLine("- Camera/XR Origin/aircraft poses captured at frame " + report.frameNumber + ".");
            builder.AppendLine("- Timing is simulator evidence only and is not Quest hardware performance evidence.");
            builder.AppendLine();
            builder.AppendLine("## Machine checks");
            builder.AppendLine();
            foreach (string line in report.checks.MarkdownLines()) builder.AppendLine("- " + line);
            if (report.errors.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("## Errors");
                builder.AppendLine();
                foreach (string error in report.errors) builder.AppendLine("- " + error.Replace("\n", " "));
            }
            return builder.ToString();
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLogMessage;
        }
    }

    [Serializable]
    internal sealed class MetaXrSimulatorSessionReport
    {
        public int schemaVersion;
        public string generatedUtc, sessionId, sessionDescription, replayPath, sceneName, architectureVersion;
        public string unityVersion, platform, graphicsDevice, graphicsApi;
        public string openXrRuntimeName, openXrRuntimeVersion, openXrApiVersion, xrLoadedDeviceName;
        public bool xrDeviceActive, stereoEnabled, displayRefreshRateAvailable, environmentContractPassed;
        public string stereoRenderingMode, qualityLevel, activeEnvironmentProfile, activeRenderProfile, demoPilotPhase;
        public float requestedRefreshRateHz, measuredSimulatorDisplayRefreshRateHz;
        public int frameNumber, captureWidth, captureHeight;
        public float unscaledTimestampSeconds, eyeSeparationMeters, meanAbsoluteStereoDifference;
        public PoseMetadata trackedCameraPose, xrOriginPose, aircraftPose;
        public EyeMetadata leftEye, rightEye;
        public XrInputDeviceMetadata hmdInputDevice, leftControllerInputDevice, rightControllerInputDevice;
        public string leftEyeImage, rightEyeImage, stereoImage;
        public ImageMetrics leftImageMetrics, rightImageMetrics;
        public int productRootCount, aircraftRigCount, enabledTrackedMainCameraCount, missingComponentCount;
        public string renderRootSignatureSha256, materialSignatureSha256, environmentContract;
        public MountainStabilityMetadata mountainStability;
        public bool viewpointUiOpenAtCapture;
        public Rect viewpointUiViewportRect;
        public float viewpointUiProtectedRegionOverlapRatio;
        public List<string> activeDebugUiObjects = new List<string>();
        public List<string> warnings = new List<string>();
        public List<string> errors = new List<string>();
        public MetaXrSimulatorChecks checks = new MetaXrSimulatorChecks();
        public bool passed;
        public string classification;
    }

    [Serializable]
    internal sealed class MetaXrSimulatorChecks
    {
        public bool metaSimulatorRuntimeConnected;
        public bool quest3RefreshRate72Hz;
        public bool trackedCameraOwnedByOpenXr;
        public bool simulatedHmdAndTouchControllersAvailable;
        public bool stereoEyePosesPlausible;
        public bool bothEyesNonBlank;
        public bool stereoSceneConsistency;
        public bool singleAuthoredProductHierarchy;
        public bool aircraftUsesCenterOfGravityPivot;
        public bool mountainIdentityImmutable;
        public bool viewpointUiDoesNotOverlapPrimaryPanel;
        public bool runwayAircraftTerrainAttached;
        public bool noMissingReferencesOrComponents;
        public bool notDominatedByDebugUi;

        public bool AllPassed()
        {
            return metaSimulatorRuntimeConnected && quest3RefreshRate72Hz && trackedCameraOwnedByOpenXr &&
                   simulatedHmdAndTouchControllersAvailable &&
                   stereoEyePosesPlausible && bothEyesNonBlank && stereoSceneConsistency &&
                   singleAuthoredProductHierarchy && aircraftUsesCenterOfGravityPivot &&
                   mountainIdentityImmutable && viewpointUiDoesNotOverlapPrimaryPanel &&
                   runwayAircraftTerrainAttached && noMissingReferencesOrComponents && notDominatedByDebugUi;
        }

        public IEnumerable<string> MarkdownLines()
        {
            yield return $"Meta simulator runtime connected: **{metaSimulatorRuntimeConnected}**";
            yield return $"Quest 3 simulator refresh is 72 Hz: **{quest3RefreshRate72Hz}**";
            yield return $"OpenXR owns the tracked stereo camera: **{trackedCameraOwnedByOpenXr}**";
            yield return $"HMD and both Touch controller devices available: **{simulatedHmdAndTouchControllersAvailable}**";
            yield return $"Plausible distinct stereo eye poses: **{stereoEyePosesPlausible}**";
            yield return $"Both eyes nonblank: **{bothEyesNonBlank}**";
            yield return $"Stereo scene/material consistency: **{stereoSceneConsistency}**";
            yield return $"Exactly one authored product hierarchy: **{singleAuthoredProductHierarchy}**";
            yield return $"Aircraft remains centered on authored CG pivot: **{aircraftUsesCenterOfGravityPivot}**";
            yield return $"Mountain mesh/transform identity immutable: **{mountainIdentityImmutable}**";
            yield return $"Viewpoint UI outside protected primary-panel region: **{viewpointUiDoesNotOverlapPrimaryPanel}**";
            yield return $"Runway/collider/terrain contract attached: **{runwayAircraftTerrainAttached}**";
            yield return $"No missing references/components: **{noMissingReferencesOrComponents}**";
            yield return $"No active debug/training overlay: **{notDominatedByDebugUi}**";
        }
    }

    [Serializable]
    internal sealed class PoseMetadata
    {
        public Vector3 worldPosition;
        public Vector3 worldEulerAngles;
        public Vector3 localPosition;
        public Vector3 localEulerAngles;

        public static PoseMetadata FromTransform(Transform transform)
        {
            return transform == null ? null : new PoseMetadata
            {
                worldPosition = transform.position,
                worldEulerAngles = transform.eulerAngles,
                localPosition = transform.localPosition,
                localEulerAngles = transform.localEulerAngles
            };
        }
    }

    [Serializable]
    internal sealed class EyeMetadata
    {
        public Vector3 worldPosition;
        public Vector3 worldEulerAngles;
        public Matrix4x4 viewMatrix;
        public Matrix4x4 projectionMatrix;

        public static EyeMetadata FromMatrices(Matrix4x4 view, Matrix4x4 projection)
        {
            Matrix4x4 world = view.inverse;
            Quaternion rotation = Quaternion.LookRotation(-world.GetColumn(2), world.GetColumn(1));
            return new EyeMetadata
            {
                worldPosition = world.GetColumn(3),
                worldEulerAngles = rotation.eulerAngles,
                viewMatrix = view,
                projectionMatrix = projection
            };
        }
    }

    [Serializable]
    internal sealed class XrInputDeviceMetadata
    {
        public bool valid;
        public string name, manufacturer, serialNumber, characteristics;
        public bool trackedStateAvailable, isTracked, positionAvailable, rotationAvailable;
        public Vector3 position;
        public Vector3 rotationEulerAngles;

        public static XrInputDeviceMetadata FromDevice(InputDevice device)
        {
            XrInputDeviceMetadata metadata = new XrInputDeviceMetadata
            {
                valid = device.isValid,
                name = device.name ?? string.Empty,
                manufacturer = device.manufacturer ?? string.Empty,
                serialNumber = device.serialNumber ?? string.Empty,
                characteristics = device.characteristics.ToString()
            };
            metadata.trackedStateAvailable = device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked);
            metadata.isTracked = tracked;
            metadata.positionAvailable = device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position);
            metadata.position = position;
            metadata.rotationAvailable = device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation);
            metadata.rotationEulerAngles = rotation.eulerAngles;
            return metadata;
        }
    }

    [Serializable]
    internal sealed class MountainStabilityMetadata
    {
        public bool passed, immutableTransforms, immutableMeshes, immutableRendererSet;
        public bool noTerrainLodOrDither, noCameraFacingOrDistanceScaling, oneMountainSource;
        public int sampledFrameCount, rendererSampleCount, activeLegacyMountainRendererCount;
        public float capturedActiveSeconds;
        public string classification;
        public List<MountainRendererBaseline> rendererBaselines = new List<MountainRendererBaseline>();
        public List<string> violations = new List<string>();

        public static MountainStabilityMetadata FromReport(MountainTemporalStabilityReport report)
        {
            if (report == null) return null;
            return new MountainStabilityMetadata
            {
                passed = report.passed,
                immutableTransforms = report.immutableTransforms,
                immutableMeshes = report.immutableMeshes,
                immutableRendererSet = report.immutableRendererSet,
                noTerrainLodOrDither = report.noTerrainLodOrDither,
                noCameraFacingOrDistanceScaling = report.noCameraFacingOrDistanceScaling,
                oneMountainSource = report.oneMountainSource,
                sampledFrameCount = report.sampledFrameCount,
                rendererSampleCount = report.rendererSampleCount,
                activeLegacyMountainRendererCount = report.activeLegacyMountainRendererCount,
                capturedActiveSeconds = report.capturedActiveSeconds,
                classification = report.classification,
                rendererBaselines = report.rendererBaselines ?? new List<MountainRendererBaseline>(),
                violations = report.violations ?? new List<string>()
            };
        }
    }

    [Serializable]
    internal sealed class ImageMetrics
    {
        public float meanLuminance, luminanceStandardDeviation, nonBlackPixelFraction;
        public bool blank;

        public static ImageMetrics Measure(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            int stride = Mathf.Max(1, pixels.Length / 150000);
            double sum = 0d, sumSquares = 0d;
            int nonBlack = 0, count = 0;
            for (int i = 0; i < pixels.Length; i += stride)
            {
                double luminance = (0.2126d * pixels[i].r + 0.7152d * pixels[i].g + 0.0722d * pixels[i].b) / 255d;
                sum += luminance;
                sumSquares += luminance * luminance;
                if (luminance > 0.01d) nonBlack++;
                count++;
            }
            float mean = count > 0 ? (float)(sum / count) : 0f;
            float variance = count > 0 ? Mathf.Max(0f, (float)(sumSquares / count) - mean * mean) : 0f;
            float standardDeviation = Mathf.Sqrt(variance);
            float fraction = count > 0 ? (float)nonBlack / count : 0f;
            return new ImageMetrics
            {
                meanLuminance = mean,
                luminanceStandardDeviation = standardDeviation,
                nonBlackPixelFraction = fraction,
                blank = count == 0 || fraction < 0.01f || standardDeviation < 0.002f
            };
        }
    }
#endif
}
