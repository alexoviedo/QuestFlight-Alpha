using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEngine;

namespace QuestFlight.Product
{
    public enum ProductMapBoundaryRecoveryPhase
    {
        Idle = 0,
        FadingOut = 1,
        Stabilizing = 2,
        FadingIn = 3,
        Failed = 4
    }

    /// <summary>
    /// Free Flight comfort recovery for the finite authored terrain. It detects the
    /// boundary before native JSBSim can lose its required terrain sample, then delegates
    /// the relocation to the sole serialized flight authority. It never writes flight,
    /// aircraft, XR, camera, seat, or calibration transforms directly. The component is
    /// explicitly authored on the production input root and never constructs replacements.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(600)]
    public sealed class ProductMapBoundaryRecovery : MonoBehaviour
    {
        private static readonly Vector4 IdentityColorScale = Vector4.one;
        private static readonly Vector4 ZeroColorOffset = Vector4.zero;

        [Header("Authored product references")]
        [SerializeField] private ProductFlightModeCoordinator modeCoordinator;
        [SerializeField] private ProductControlInput productInput;
        [SerializeField] private FlightDynamicsCoordinator flightCoordinator;
        [SerializeField] private ProductionTerrainSurfaceAsset terrainSurface;

        [Header("Comfort recovery policy")]
        [SerializeField, Min(250f)] private float triggerInsetMeters = 1500f;
        [SerializeField, Min(500f)] private float returnInsetMeters = 3000f;
        [SerializeField, Min(100f)] private float minimumRecoveryAglMeters = 300f;
        [SerializeField, Min(45f)] private float minimumRecoveryAirspeedKnots = 70f;
        [SerializeField, Min(0.05f)] private float fadeOutSeconds = 0.25f;
        [SerializeField, Min(0.1f)] private float stabilizationSeconds = 0.50f;
        [SerializeField, Min(0.05f)] private float fadeInSeconds = 0.35f;

        public ProductMapBoundaryRecoveryPhase Phase { get; private set; } =
            ProductMapBoundaryRecoveryPhase.Idle;
        public string LastError { get; private set; } = string.Empty;
        public int CompletedRecoveryCount { get; private set; }
        public ProductFlightModeCoordinator AuthoredModeCoordinator => modeCoordinator;
        public ProductControlInput AuthoredProductInput => productInput;
        public FlightDynamicsCoordinator AuthoredFlightCoordinator => flightCoordinator;
        public ProductionTerrainSurfaceAsset AuthoredTerrainSurface => terrainSurface;
        public float TriggerInsetMeters => triggerInsetMeters;
        public float ReturnInsetMeters => returnInsetMeters;
        public bool HasAuthoredReferences =>
            modeCoordinator != null &&
            productInput != null &&
            flightCoordinator != null &&
            terrainSurface != null &&
            modeCoordinator.AuthoredProductInput == productInput &&
            modeCoordinator.AuthoredFlightCoordinator == flightCoordinator &&
            flightCoordinator.controls == productInput &&
            flightCoordinator.terrainSurface == terrainSurface &&
            ProductMapBoundaryRecoveryPolicy.HasValidInsets(
                terrainSurface.macroSizeMeters,
                triggerInsetMeters,
                returnInsetMeters);

        private float _phaseElapsed;
        private double _resetSimulationTime;
        private bool _ownsInputSuppression;
        private float _appliedFadeOpacity = -1f;

        public void ConfigureAuthoredReferences(
            ProductFlightModeCoordinator authoredModeCoordinator,
            ProductControlInput authoredProductInput,
            FlightDynamicsCoordinator authoredFlightCoordinator,
            ProductionTerrainSurfaceAsset authoredTerrainSurface)
        {
            modeCoordinator = authoredModeCoordinator;
            productInput = authoredProductInput;
            flightCoordinator = authoredFlightCoordinator;
            terrainSurface = authoredTerrainSurface;
        }

        private void Awake()
        {
            RestoreCompositorIdentity();
            if (HasAuthoredReferences)
                return;

            LastError = "Map-boundary recovery references or terrain insets are invalid. Re-author the production world prefab.";
            Debug.LogError("[QuestFlight][MapBoundaryRecovery] " + LastError, this);
            enabled = false;
        }

        private void Update()
        {
            if (Phase == ProductMapBoundaryRecoveryPhase.Failed)
                return;

            if (Phase == ProductMapBoundaryRecoveryPhase.Idle)
            {
                if (modeCoordinator.State !=
                        ProductFlightModeCoordinator.FlightModeState.FreeFlight ||
                    !flightCoordinator.enabled ||
                    !flightCoordinator.IsInitialized)
                    return;

                FlightDynamicsState state = flightCoordinator.CurrentState;
                if (!state.IsFinite ||
                    !ProductMapBoundaryRecoveryPolicy.ShouldRecover(
                        state.positionUnityMeters,
                        terrainSurface.macroMinimumEnuMeters,
                        terrainSurface.macroSizeMeters,
                        triggerInsetMeters))
                    return;

                BeginRecovery(state);
            }

            AdvanceRecovery(Mathf.Max(0f, Time.unscaledDeltaTime));
        }

        private void BeginRecovery(in FlightDynamicsState state)
        {
            if (productInput.FlightLessonInputSuppressed)
            {
                FailClosed("Free Flight input was already suppressed when boundary recovery began.");
                return;
            }

            if (!ProductMapBoundaryRecoveryPolicy.TryBuildRecoveryInitialConditions(
                    state,
                    terrainSurface,
                    returnInsetMeters,
                    minimumRecoveryAglMeters,
                    minimumRecoveryAirspeedKnots,
                    out FlightDynamicsInitialConditions initialConditions,
                    out string failure))
            {
                FailClosed(failure);
                return;
            }

            productInput.ReleaseAllTouchOverrides();
            productInput.SetFlightLessonInputSuppressed(true);
            _ownsInputSuppression = true;
            Phase = ProductMapBoundaryRecoveryPhase.FadingOut;
            _phaseElapsed = 0f;
            _pendingInitialConditions = initialConditions;
            Debug.LogWarning(
                $"[QuestFlight][MapBoundaryRecovery] Boundary approached at " +
                $"{state.positionUnityMeters:F1}; returning inside the authored flight area.",
                this);
        }

        private FlightDynamicsInitialConditions _pendingInitialConditions;

        private void AdvanceRecovery(float deltaTime)
        {
            switch (Phase)
            {
                case ProductMapBoundaryRecoveryPhase.FadingOut:
                    _phaseElapsed += deltaTime;
                    SetComfortFade(Mathf.Clamp01(_phaseElapsed /
                        Mathf.Max(0.01f, fadeOutSeconds)));
                    if (_phaseElapsed < fadeOutSeconds)
                        return;
                    SetComfortFade(1f);
                    if (!ApplyAuthoritativeRecovery())
                        return;
                    Phase = ProductMapBoundaryRecoveryPhase.Stabilizing;
                    _phaseElapsed = 0f;
                    break;

                case ProductMapBoundaryRecoveryPhase.Stabilizing:
                    _phaseElapsed += deltaTime;
                    if (!flightCoordinator.enabled ||
                        !flightCoordinator.IsInitialized ||
                        !flightCoordinator.CurrentState.IsFinite)
                    {
                        FailClosed("Native JSBSim did not remain live after boundary recovery.");
                        return;
                    }
                    if (_phaseElapsed < stabilizationSeconds)
                        return;
                    if (flightCoordinator.CurrentState.simulationTimeSeconds <=
                        _resetSimulationTime)
                    {
                        FailClosed("Native JSBSim did not advance after boundary recovery.");
                        return;
                    }
                    Phase = ProductMapBoundaryRecoveryPhase.FadingIn;
                    _phaseElapsed = 0f;
                    break;

                case ProductMapBoundaryRecoveryPhase.FadingIn:
                    _phaseElapsed += deltaTime;
                    SetComfortFade(1f - Mathf.Clamp01(_phaseElapsed /
                        Mathf.Max(0.01f, fadeInSeconds)));
                    if (_phaseElapsed < fadeInSeconds)
                        return;
                    CompleteRecovery();
                    break;
            }
        }

        private bool ApplyAuthoritativeRecovery()
        {
            productInput.PrepareForFlightLessonRecovery(
                ProductMapBoundaryRecoveryPolicy.BuildStabilizationCommand(
                    _pendingInitialConditions.engineRunning));
            if (!flightCoordinator.TryResetToInitialConditions(
                    _pendingInitialConditions))
            {
                FailClosed("Native JSBSim rejected the inward boundary recovery: " +
                    flightCoordinator.LastError);
                return false;
            }

            _resetSimulationTime = flightCoordinator.CurrentState.simulationTimeSeconds;
            return true;
        }

        private void CompleteRecovery()
        {
            ReleaseOwnedInputSuppression();
            RestoreCompositorIdentity();
            Phase = ProductMapBoundaryRecoveryPhase.Idle;
            _phaseElapsed = 0f;
            LastError = string.Empty;
            CompletedRecoveryCount++;
            Debug.Log("[QuestFlight][MapBoundaryRecovery] Free Flight resumed inside the authored flight area.", this);
        }

        private void SetComfortFade(float opacity)
        {
            float clamped = Mathf.Clamp01(opacity);
            if (Mathf.Abs(clamped - _appliedFadeOpacity) < 0.0001f)
                return;
            float scale = 1f - clamped;
            OVRManager.SetColorScaleAndOffset(
                new Vector4(scale, scale, scale, scale),
                ZeroColorOffset,
                true);
            _appliedFadeOpacity = clamped;
        }

        private void FailClosed(string failure)
        {
            LastError = string.IsNullOrWhiteSpace(failure)
                ? "Map-boundary recovery failed. Restart QuestFlight."
                : failure;
            Phase = ProductMapBoundaryRecoveryPhase.Failed;
            SetComfortFade(1f);
            Debug.LogError("[QuestFlight][MapBoundaryRecovery] " + LastError, this);
        }

        private void ReleaseOwnedInputSuppression()
        {
            if (!_ownsInputSuppression)
                return;
            productInput.SetFlightLessonInputSuppressed(false);
            _ownsInputSuppression = false;
        }

        private void RestoreCompositorIdentity()
        {
            OVRManager.SetColorScaleAndOffset(
                IdentityColorScale,
                ZeroColorOffset,
                true);
            _appliedFadeOpacity = 0f;
        }

        private void OnDisable()
        {
            ReleaseOwnedInputSuppression();
            RestoreCompositorIdentity();
        }

        private void OnDestroy()
        {
            ReleaseOwnedInputSuppression();
            RestoreCompositorIdentity();
        }
    }

    public static class ProductMapBoundaryRecoveryPolicy
    {
        public static bool HasValidInsets(
            Vector2 mapSizeMeters,
            float triggerInsetMeters,
            float returnInsetMeters)
        {
            float halfMinimumDimension =
                Mathf.Min(mapSizeMeters.x, mapSizeMeters.y) * 0.5f;
            return mapSizeMeters.x > 0f &&
                   mapSizeMeters.y > 0f &&
                   triggerInsetMeters > 0f &&
                   returnInsetMeters > triggerInsetMeters &&
                   returnInsetMeters < halfMinimumDimension;
        }

        public static bool ShouldRecover(
            Vector3 positionUnityMeters,
            Vector2 mapMinimumEnuMeters,
            Vector2 mapSizeMeters,
            float triggerInsetMeters)
        {
            Vector2 maximum = mapMinimumEnuMeters + mapSizeMeters;
            return positionUnityMeters.x <=
                       mapMinimumEnuMeters.x + triggerInsetMeters ||
                   positionUnityMeters.x >= maximum.x - triggerInsetMeters ||
                   positionUnityMeters.z <=
                       mapMinimumEnuMeters.y + triggerInsetMeters ||
                   positionUnityMeters.z >= maximum.y - triggerInsetMeters;
        }

        public static bool TryBuildRecoveryInitialConditions(
            in FlightDynamicsState state,
            ProductionTerrainSurfaceAsset terrainSurface,
            float returnInsetMeters,
            float minimumRecoveryAglMeters,
            float minimumRecoveryAirspeedKnots,
            out FlightDynamicsInitialConditions initialConditions,
            out string failure)
        {
            initialConditions = default;
            if (terrainSurface == null || !state.IsFinite)
            {
                failure = "Boundary recovery requires finite native state and the authored terrain surface.";
                return false;
            }

            Vector2 minimum = terrainSurface.macroMinimumEnuMeters;
            Vector2 maximum = minimum + terrainSurface.macroSizeMeters;
            if (!HasValidInsets(
                    terrainSurface.macroSizeMeters,
                    Mathf.Min(1f, returnInsetMeters * 0.5f),
                    returnInsetMeters))
            {
                failure = "The authored terrain cannot contain the configured boundary return inset.";
                return false;
            }

            Vector2 target = new Vector2(
                Mathf.Clamp(
                    state.positionUnityMeters.x,
                    minimum.x + returnInsetMeters,
                    maximum.x - returnInsetMeters),
                Mathf.Clamp(
                    state.positionUnityMeters.z,
                    minimum.y + returnInsetMeters,
                    maximum.y - returnInsetMeters));
            if (!terrainSurface.TrySample(
                    target.x,
                    target.y,
                    out ProductionTerrainSample terrain))
            {
                failure = $"The authored terrain has no recovery sample at ENU ({target.x:0.0}, {target.y:0.0}) m.";
                return false;
            }

            GeodeticReference origin = GeodeticReference.Kbdu;
            double terrainMslMeters = origin.altitudeMslMeters + terrain.heightMeters;
            double altitudeMslMeters = System.Math.Max(
                state.altitudeMslMeters,
                terrainMslMeters + Mathf.Max(100f, minimumRecoveryAglMeters));
            GeodeticReference geodetic = FlightFrameConversions.UnityToGeodetic(
                new Vector3(
                    target.x,
                    (float)(altitudeMslMeters - origin.altitudeMslMeters),
                    target.y),
                origin);
            double headingToCenter = Mathf.Repeat(
                Mathf.Atan2(-target.x, -target.y) * Mathf.Rad2Deg,
                360f);

            initialConditions = new FlightDynamicsInitialConditions
            {
                latitudeDegrees = geodetic.latitudeDegrees,
                longitudeDegrees = geodetic.longitudeDegrees,
                altitudeMslMeters = altitudeMslMeters,
                terrainElevationMslMeters = terrainMslMeters,
                calibratedAirspeedKnots = System.Math.Max(
                    minimumRecoveryAirspeedKnots,
                    System.Math.Min(130d, state.calibratedAirspeedKnots)),
                headingDegrees = headingToCenter,
                pitchDegrees = 2d,
                bankDegrees = 0d,
                flightPathAngleDegrees = 0d,
                engineRunning = state.engineRpm >= 300d
            };
            failure = string.Empty;
            return true;
        }

        public static FlightLessonControlCommand BuildStabilizationCommand(
            bool engineRunning)
        {
            return new FlightLessonControlCommand
            {
                throttle = engineRunning ? 0.65f : 0f,
                mixture = engineRunning ? 1f : 0f,
                trim = 0.05f
            };
        }
    }
}
