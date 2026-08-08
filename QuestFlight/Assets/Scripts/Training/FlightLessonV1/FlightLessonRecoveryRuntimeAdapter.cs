using QuestFlight.Product;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using UnityEngine;

namespace QuestFlightLab.Training.FlightLessonV1
{
    /// <summary>
    /// Authored product seam between the pure recovery policy and the single flight authority.
    /// It samples immutable ENU terrain data and never constructs or moves product/XR objects.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(500)]
    public sealed class FlightLessonRecoveryRuntimeAdapter : MonoBehaviour
    {
        private static readonly Vector4 IdentityColorScale = Vector4.one;
        private static readonly Vector4 ZeroColorOffset = Vector4.zero;

        [Header("Authored product references")]
        [SerializeField] private ProductDemoPilot demoPilot;
        [SerializeField] private ProductControlInput productInput;
        [SerializeField] private FlightDynamicsCoordinator flightCoordinator;
        [SerializeField] private ProductionTerrainSurfaceAsset terrainSurface;

        public ProductDemoPilot AuthoredDemoPilot => demoPilot;
        public ProductControlInput AuthoredProductInput => productInput;
        public FlightDynamicsCoordinator AuthoredFlightCoordinator => flightCoordinator;
        public ProductionTerrainSurfaceAsset AuthoredTerrainSurface => terrainSurface;
        public FlightLessonRecoveryPhase RecoveryPhase => recoveryStateMachine.Phase;
        public string LastFailure { get; private set; } = string.Empty;
        public FlightLessonCrashReason LastCrashReason { get; private set; } =
            FlightLessonCrashReason.None;

        private readonly FlightLessonCrashClassifier crashClassifier =
            new FlightLessonCrashClassifier();
        private readonly FlightLessonRecoveryStateMachine recoveryStateMachine =
            new FlightLessonRecoveryStateMachine();

        private FlightLessonCrashSettings crashSettings;
        private FlightLessonRecoveryTimings recoveryTimings;
        private bool referencesValidated;
        private bool initialized;
        private bool suppressionApplied;
        private float appliedFadeOpacity = -1f;

        public void ConfigureAuthoredReferences(
            ProductDemoPilot authoredDemoPilot,
            ProductControlInput authoredProductInput,
            FlightDynamicsCoordinator authoredFlightCoordinator,
            ProductionTerrainSurfaceAsset authoredTerrainSurface)
        {
            demoPilot = authoredDemoPilot;
            productInput = authoredProductInput;
            flightCoordinator = authoredFlightCoordinator;
            terrainSurface = authoredTerrainSurface;
        }

        public bool HasAuthoredReferences(out string failure)
        {
            if (demoPilot == null || productInput == null || flightCoordinator == null ||
                terrainSurface == null)
            {
                failure = "Demo pilot, product input, flight coordinator, and terrain surface must all be serialized.";
                return false;
            }

            if (!demoPilot.HasAuthoredSources || demoPilot.AuthoredProductInput != productInput)
            {
                failure = "The recovery adapter and demo pilot must share the same authored product input.";
                return false;
            }

            if (demoPilot.AuthoredRouteProfile == null ||
                demoPilot.AuthoredRouteProfile.Definition == null)
            {
                failure = "The recovery adapter requires one serialized route profile.";
                return false;
            }

            if (!demoPilot.AuthoredRouteProfile.Definition.TryValidate(
                    out string routeFailure))
            {
                failure = "The serialized route profile is invalid: " + routeFailure;
                return false;
            }

            if (!productInput.HasAuthoredSources || flightCoordinator.controls != productInput)
            {
                failure = "The recovery adapter, input boundary, and flight authority must reference one authored input source.";
                return false;
            }

            if (!flightCoordinator.HasAuthoredReferences(out string coordinatorFailure))
            {
                failure = "Flight authority is not valid: " + coordinatorFailure;
                return false;
            }

            if (terrainSurface.layers == null || terrainSurface.layers.Length == 0)
            {
                failure = "The authored production terrain surface has no sample layers.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private void Start()
        {
            if (!HasAuthoredReferences(out string failure))
            {
                FailClosed(failure);
                return;
            }

            crashSettings = FlightLessonCrashSettings.ConservativeTrainingDefaults;
            recoveryTimings = FlightLessonRecoveryTimings.Default;
            RestoreCompositorIdentity();
            referencesValidated = true;
            initialized = false;
        }

        private void FixedUpdate()
        {
            if (!referencesValidated || !demoPilot.IsRunning)
                return;

            // Native-development initialization may yield while copying runtime data.
            // Never classify or recover from the coordinator's default state.
            if (!initialized)
            {
                if (!flightCoordinator.IsInitialized)
                    return;
                crashClassifier.Reset(
                    flightCoordinator.CurrentState.weightOnWheels);
                initialized = true;
            }

            float deltaTime = Mathf.Max(0f, Time.fixedUnscaledDeltaTime);
            if (!recoveryStateMachine.IsActive)
            {
                FlightDynamicsState state = flightCoordinator.CurrentState;
                TryMapTerrainSample(
                    terrainSurface,
                    state.positionUnityMeters,
                    out FlightLessonTerrainSample terrain);
                FlightLessonAircraftSafetyState safety = BuildSafetyState(state);
                FlightLessonCrashReason reason = crashClassifier.Evaluate(
                    safety,
                    terrain,
                    deltaTime,
                    crashSettings);
                if (reason != FlightLessonCrashReason.None)
                {
                    LastCrashReason = reason;
                    Debug.LogWarning(
                        $"[QuestFlight][Recovery] Trigger={reason} " +
                        $"Position={state.positionUnityMeters:F3} " +
                        $"WOW={state.weightOnWheels} GS={state.groundSpeedKnots:F1}kt " +
                        $"CAS={state.calibratedAirspeedKnots:F1}kt " +
                        $"AGL={state.altitudeAglMeters:F2}m VS={state.verticalSpeedFeetPerMinute:F0}fpm " +
                        $"Pitch={state.pitchDegrees:F1}deg Bank={state.bankDegrees:F1}deg " +
                        $"Controls[Ail={flightCoordinator.LastAppliedControls.aileron:F2}," +
                        $"Ele={flightCoordinator.LastAppliedControls.elevator:F2}," +
                        $"Rud={flightCoordinator.LastAppliedControls.rudder:F2}," +
                        $"Thr={flightCoordinator.LastAppliedControls.throttle:F2}," +
                        $"Trim={flightCoordinator.LastAppliedControls.trim:F2}] " +
                        $"TerrainValid={terrain.isValid} TerrainY={terrain.heightUnityMeters:F3} " +
                        $"RunwayOrApron={terrain.isRunwayOrApron} Water={terrain.isWater} " +
                        productInput.BuildFlightLessonFailureDiagnostic() + " " +
                        demoPilot.BuildFailureDiagnostic(),
                        this);
                    FlightLessonRouteDefinition route = demoPilot.RouteDefinition;
                    FlightLessonProgressState progress = demoPilot.ProgressState;
                    if (!recoveryStateMachine.TryBegin(
                            reason,
                            route,
                            progress,
                            recoveryTimings))
                    {
                        FailClosed("Recovery policy could not select an authored checkpoint.");
                        return;
                    }
                }
            }

            if (!recoveryStateMachine.IsActive)
                return;

            FlightLessonRecoveryOutput output = recoveryStateMachine.Step(deltaTime);
            ApplyRecoveryOutput(output);
        }

        private void ApplyRecoveryOutput(in FlightLessonRecoveryOutput output)
        {
            if (output.cancelControlOwnership)
                productInput.ReleaseAllTouchOverrides();

            SetSuppression(output.suppressPlayerInput);
            SetComfortFade(output.comfortFadeOpacity);

            if (!output.requestAuthoritativeReset)
                return;

            bool succeeded = TryApplyAuthoritativeRecovery(output.request);
            recoveryStateMachine.AcknowledgeAuthoritativeReset(succeeded);
            if (!succeeded)
                FailClosed("The sole flight authority rejected the authored recovery checkpoint.");
        }

        private bool TryApplyAuthoritativeRecovery(in FlightLessonRecoveryRequest request)
        {
            FlightLessonRouteDefinition route = demoPilot.RouteDefinition;
            int segmentIndex = request.checkpoint.resumeSegmentIndex;
            if (route?.segments == null || segmentIndex < 0 || segmentIndex >= route.segments.Length)
                return false;

            if (!TryBuildInitialConditions(
                    request.checkpoint,
                    terrainSurface,
                    out FlightDynamicsInitialConditions initialConditions))
                return false;

            productInput.ReleaseAllTouchOverrides();
            if (!flightCoordinator.TryResetToInitialConditions(initialConditions))
                return false;

            if (!demoPilot.RestoreLessonSegment(segmentIndex))
                return false;

            crashClassifier.Reset(flightCoordinator.CurrentState.weightOnWheels);
            return true;
        }

        private void SetSuppression(bool suppressed)
        {
            if (suppressionApplied == suppressed)
                return;

            demoPilot.SetGuidanceSuppressed(suppressed);
            suppressionApplied = suppressed;
        }

        private void SetComfortFade(float opacity)
        {
            float clamped = Mathf.Clamp01(opacity);
            if (Mathf.Abs(clamped - appliedFadeOpacity) < 0.0001f)
                return;

            float scale = 1f - clamped;
            OVRManager.SetColorScaleAndOffset(
                new Vector4(scale, scale, scale, scale),
                ZeroColorOffset,
                true);
            appliedFadeOpacity = clamped;
        }

        private void OnDisable()
        {
            ReleaseRecoverySuppression();
            RestoreCompositorIdentity();
            initialized = false;
        }

        private void OnDestroy()
        {
            ReleaseRecoverySuppression();
            RestoreCompositorIdentity();
        }

        private void ReleaseRecoverySuppression()
        {
            if (suppressionApplied && demoPilot != null)
                demoPilot.SetGuidanceSuppressed(false);
            else if (productInput != null)
                productInput.SetFlightLessonInputSuppressed(false);
            suppressionApplied = false;
        }

        private void RestoreCompositorIdentity()
        {
            OVRManager.SetColorScaleAndOffset(
                IdentityColorScale,
                ZeroColorOffset,
                true);
            appliedFadeOpacity = 0f;
        }

        private void FailClosed(string failure)
        {
            LastFailure = failure;
            referencesValidated = false;
            ReleaseRecoverySuppression();
            RestoreCompositorIdentity();
            initialized = false;
            enabled = false;
            Debug.LogError("[QuestFlight][Recovery] " + failure, this);
        }

        public static FlightLessonAircraftSafetyState BuildSafetyState(
            in FlightDynamicsState state)
        {
            return new FlightLessonAircraftSafetyState
            {
                positionUnityMeters = state.positionUnityMeters,
                rotationUnity = state.rotationUnity,
                velocityUnityMetersPerSecond = state.velocityUnityMetersPerSecond,
                altitudeAglMeters = (float)state.altitudeAglMeters,
                calibratedAirspeedKnots = (float)state.calibratedAirspeedKnots,
                groundSpeedKnots = (float)state.groundSpeedKnots,
                verticalSpeedFeetPerMinute = (float)state.verticalSpeedFeetPerMinute,
                pitchDegrees = (float)state.pitchDegrees,
                bankDegrees = (float)state.bankDegrees,
                weightOnWheels = state.weightOnWheels
            };
        }

        public static bool TryMapTerrainSample(
            ProductionTerrainSurfaceAsset surface,
            Vector3 positionUnityMeters,
            out FlightLessonTerrainSample terrain)
        {
            if (surface == null ||
                !surface.TrySample(
                    positionUnityMeters.x,
                    positionUnityMeters.z,
                    out ProductionTerrainSample sample))
            {
                terrain = default;
                return false;
            }

            terrain = new FlightLessonTerrainSample
            {
                isValid = true,
                heightUnityMeters = sample.heightMeters,
                normalUnity = sample.normal,
                isRunwayOrApron = sample.IsRunwayOrApron,
                isWater = sample.IsWater
            };
            return true;
        }

        public static bool TryBuildInitialConditions(
            in FlightLessonRecoveryCheckpoint checkpoint,
            ProductionTerrainSurfaceAsset surface,
            out FlightDynamicsInitialConditions initialConditions)
        {
            initialConditions = default;
            if (surface == null || !IsFinite(checkpoint.positionUnityMeters) ||
                !IsFinite(checkpoint.altitudeMslFeet) ||
                !IsFinite(checkpoint.calibratedAirspeedKnots) ||
                !IsFinite(checkpoint.headingDegreesTrue) ||
                !IsFinite(checkpoint.pitchDegrees) ||
                !IsFinite(checkpoint.bankDegrees) ||
                !surface.TrySample(
                    checkpoint.positionUnityMeters.x,
                    checkpoint.positionUnityMeters.z,
                    out ProductionTerrainSample terrain))
                return false;

            GeodeticReference origin = GeodeticReference.Kbdu;
            double targetAltitudeMeters =
                checkpoint.altitudeMslFeet * FlightFrameConversions.FeetToMeters;
            Vector3 targetEnu = new Vector3(
                checkpoint.positionUnityMeters.x,
                (float)(targetAltitudeMeters - origin.altitudeMslMeters),
                checkpoint.positionUnityMeters.z);
            GeodeticReference targetGeodetic =
                FlightFrameConversions.UnityToGeodetic(targetEnu, origin);
            GeodeticReference terrainGeodetic = FlightFrameConversions.UnityToGeodetic(
                new Vector3(
                    checkpoint.positionUnityMeters.x,
                    terrain.heightMeters,
                    checkpoint.positionUnityMeters.z),
                origin);

            if (targetAltitudeMeters < terrainGeodetic.altitudeMslMeters)
                return false;

            initialConditions = new FlightDynamicsInitialConditions
            {
                latitudeDegrees = targetGeodetic.latitudeDegrees,
                longitudeDegrees = targetGeodetic.longitudeDegrees,
                altitudeMslMeters = targetAltitudeMeters,
                terrainElevationMslMeters = terrainGeodetic.altitudeMslMeters,
                calibratedAirspeedKnots = checkpoint.calibratedAirspeedKnots,
                headingDegrees = checkpoint.headingDegreesTrue,
                pitchDegrees = checkpoint.pitchDegrees,
                bankDegrees = checkpoint.bankDegrees,
                flightPathAngleDegrees = 0d,
                engineRunning = checkpoint.engineRunning
            };
            return true;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
}
