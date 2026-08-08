using System;
using System.Collections;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlightLab.Flight.Backends
{
    /// <summary>
    /// Sole product fixed-step authority. The serialized release request remains
    /// UnityPrototype. Editor/development players may prefer the promoted native
    /// package, with fail-closed fallback under this same authority lease.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlightDynamicsCoordinator : MonoBehaviour
    {
        [Header("Single product authority")]
        public FlightDynamicsBackendKind requestedBackend = FlightDynamicsBackendKind.UnityPrototype;
        [Tooltip("Editor/development builds only. Release builds always resolve to UnityPrototype.")]
        public bool preferNativeInDevelopment = true;
        public Transform simulationRoot;
        public Transform presentationRoot;
        public AircraftState aircraftState;
        public SimpleAircraftPhysics unityPrototype;
        public Rigidbody authoritativeRigidbody;
        public ProductControlInput controls;

        [Header("Authored initial conditions")]
        public FlightDynamicsInitialConditionProvider initialConditionProvider;

        [Header("Fixed step")]
        [Min(30f)] public float simulationRateHz = 120f;
        [Range(1, 16)] public int maxStepsPerFixedUpdate = 8;

        public FlightDynamicsBackendKind ActiveBackendKind =>
            _backend?.Kind ?? FlightDynamicsBackendKind.UnityPrototype;
        public bool IsInitialized => _backend != null && _backend.IsInitialized;
        public bool CoordinatorOwnsPresentationInterpolation => ShouldCoordinatorInterpolate(simulationRoot, presentationRoot);
        public string LastError { get; private set; } = string.Empty;
        public string FallbackReason { get; private set; } = string.Empty;
        public FlightDynamicsState CurrentState => _currentState;
        public FlightDynamicsControlSnapshot LastAppliedControls { get; private set; } =
            FlightDynamicsControlSnapshot.NeutralIdle;
        public FlightDynamicsInitialConditions LastResetInitialConditions { get; private set; }
        public double DroppedSimulationSeconds => _accumulator?.DroppedSeconds ?? 0.0;

        private IFlightDynamicsBackend _backend;
        private FlightDynamicsAuthorityLease _authorityLease;
        private FixedStepAccumulator _accumulator;
        private FlightDynamicsState _previousState;
        private FlightDynamicsState _currentState;
        private FlightDynamicsInitialConditions _configuredInitialConditions;
        private bool _hasConfiguredInitialConditions;
        private Action<double> _stepBackendAction;
        private string _preparedNativeDataRoot = string.Empty;
        private string _nativePreparationError = string.Empty;
        private bool _authoredPrototypeEnabled;
        private bool _prototypeEnabledStateCaptured;
        private static readonly AircraftControlState NeutralIdleControls = AircraftControlState.Neutral(0.10f);

        public static bool DevelopmentBuildAllowsNative
        {
            get
            {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && QUESTFLIGHT_ENABLE_NATIVE_JSBSIM && !QUESTFLIGHT_FORCE_UNITY_PROTOTYPE
                return true;
#else
                return false;
#endif
            }
        }

        private void Awake()
        {
            CapturePrototypeEnabledState();
            if (!HasAuthoredReferences(out string error))
            {
                LastError = error;
                Debug.LogError("[QuestFlight][FlightAuthority] " + error, this);
                enabled = false;
                return;
            }
            _stepBackendAction = StepBackendUnchecked;
            EnforceSinglePresentationOwner();
            if (ResolveEffectiveBackend(requestedBackend, preferNativeInDevelopment,
                    DevelopmentBuildAllowsNative) == FlightDynamicsBackendKind.JSBSimNativeDevelopment)
            {
                unityPrototype.enabled = false;
            }
        }

        public bool HasAuthoredReferences(out string error)
        {
            if (simulationRoot == null || presentationRoot == null || aircraftState == null ||
                unityPrototype == null || authoritativeRigidbody == null || controls == null ||
                initialConditionProvider == null)
            {
                error = "All flight-authority references must be serialized in the product prefab.";
                return false;
            }
            if (simulationRoot != transform || unityPrototype.transform != simulationRoot ||
                aircraftState.transform != simulationRoot || authoritativeRigidbody.transform != simulationRoot)
            {
                error = "Flight-authority components must share AircraftSimulationRoot.";
                return false;
            }
            if (requestedBackend != FlightDynamicsBackendKind.UnityPrototype)
            {
                error = "The serialized product coordinator must request UnityPrototype.";
                return false;
            }
            if (!preferNativeInDevelopment)
            {
                error = "The product prefab must serialize the development-only native preference.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private IEnumerator Start()
        {
            FlightDynamicsBackendKind desired = ResolveEffectiveBackend(
                requestedBackend,
                preferNativeInDevelopment,
                DevelopmentBuildAllowsNative);
            if (desired == FlightDynamicsBackendKind.JSBSimNativeDevelopment &&
                !JSBSimRuntimeData.TryGetPreparedDataRoot(out _preparedNativeDataRoot, out _nativePreparationError))
            {
                bool prepared = false;
                yield return JSBSimRuntimeData.Prepare((success, root, error) =>
                {
                    prepared = success;
                    _preparedNativeDataRoot = root ?? string.Empty;
                    _nativePreparationError = error ?? string.Empty;
                });
                if (!prepared && string.IsNullOrWhiteSpace(_nativePreparationError))
                    _nativePreparationError = "Native JSBSim runtime data preparation failed.";
            }
            InitializeSelectedBackend();
        }

        public bool InitializeSelectedBackend()
        {
            CapturePrototypeEnabledState();
            ShutdownBackend();
            FallbackReason = string.Empty;
            LastError = string.Empty;
            RefreshConfiguredInitialConditions();
            if (!FlightDynamicsAuthority.TryAcquire(simulationRoot, this, out _authorityLease, out string authorityError))
                return Fail(authorityError);

            double fixedStep = 1.0 / Mathf.Max(30f, simulationRateHz);
            _accumulator = new FixedStepAccumulator(fixedStep, maxStepsPerFixedUpdate);
            FlightDynamicsBackendContext context = CreateContext(fixedStep);
            FlightDynamicsBackendKind desired = ResolveEffectiveBackend(
                requestedBackend,
                preferNativeInDevelopment,
                DevelopmentBuildAllowsNative);

            if (desired == FlightDynamicsBackendKind.JSBSimNativeDevelopment)
            {
                if (string.IsNullOrWhiteSpace(_preparedNativeDataRoot))
                    JSBSimRuntimeData.TryGetPreparedDataRoot(out _preparedNativeDataRoot, out _nativePreparationError);
                if (!string.IsNullOrWhiteSpace(_preparedNativeDataRoot))
                {
                    unityPrototype.enabled = false;
                    IFlightDynamicsBackend native = new JSBSimNativeDevelopmentBackend(_preparedNativeDataRoot);
                    if (TryInitializeBackend(native, context, _configuredInitialConditions, out string nativeError))
                    {
                        CompleteInitialization(fixedStep);
                        return true;
                    }
                    native.Dispose();
                    FallbackReason = "Native JSBSim initialization failed: " + nativeError;
                }
                else
                {
                    FallbackReason = "Native JSBSim data preparation failed: " + _nativePreparationError;
                }
                Debug.LogWarning("[QuestFlight][FlightAuthority] " + FallbackReason +
                                 " Falling back to Unity prototype.", this);
            }

            RestorePrototypeEnabledState();
            IFlightDynamicsBackend prototype = new UnityPrototypeFlightBackend();
            if (!TryInitializeBackend(prototype, context, _configuredInitialConditions, out string prototypeError))
            {
                prototype.Dispose();
                return ReleaseAndFail(prototypeError);
            }
            CompleteInitialization(fixedStep);
            return true;
        }

        public static FlightDynamicsBackendKind ResolveEffectiveBackend(
            FlightDynamicsBackendKind serializedRequest,
            bool preferNativeInDevelopment,
            bool developmentBuildAllowsNative)
        {
            if (!developmentBuildAllowsNative) return FlightDynamicsBackendKind.UnityPrototype;
            if (preferNativeInDevelopment) return FlightDynamicsBackendKind.JSBSimNativeDevelopment;
            return serializedRequest == FlightDynamicsBackendKind.JSBSimNativeDevelopment
                ? FlightDynamicsBackendKind.JSBSimNativeDevelopment
                : FlightDynamicsBackendKind.UnityPrototype;
        }

        private void FixedUpdate()
        {
            if (!IsInitialized) return;
            if (controls != null && controls.ConsumeResetRequest())
            {
                ResetToConfiguredInitialConditions();
                return;
            }
            if (controls != null && controls.Paused) return;
            ApplyControls(controls != null ? controls.Current : NeutralIdleControls);
            _accumulator.Consume(Time.fixedDeltaTime, _stepBackendAction);
        }

        private void LateUpdate()
        {
            EnforceSinglePresentationOwner();
            if (!IsInitialized || !CoordinatorOwnsPresentationInterpolation) return;
            float alpha = (float)_accumulator.InterpolationAlpha;
            presentationRoot.SetPositionAndRotation(
                Vector3.LerpUnclamped(_previousState.positionUnityMeters, _currentState.positionUnityMeters, alpha),
                Quaternion.SlerpUnclamped(_previousState.rotationUnity, _currentState.rotationUnity, alpha));
        }

        public bool StepForTest(AircraftControlState controlState, double fixedDeltaTimeSeconds)
        {
            if (!IsInitialized) return false;
            ApplyControls(controlState ?? NeutralIdleControls);
            return StepBackend(fixedDeltaTimeSeconds);
        }

        public void RefreshConfiguredInitialConditions()
        {
            _configuredInitialConditions = initialConditionProvider != null
                ? initialConditionProvider.Build(GeodeticReference.Kbdu)
                : FlightDynamicsInitialConditions.KbduRunway();
            _hasConfiguredInitialConditions = true;
        }

        public bool ResetToConfiguredInitialConditions()
        {
            if (!IsInitialized) return Fail("Cannot reset flight dynamics before backend initialization.");
            if (!_hasConfiguredInitialConditions) RefreshConfiguredInitialConditions();
            return ResetToInitialConditions(_configuredInitialConditions, true);
        }

        /// <summary>
        /// Applies a caller-provided recovery state through the one active backend and the
        /// existing authority lease. This is the only supported in-flight recovery seam;
        /// callers must not move the simulation root, presentation root, XR Origin, or camera.
        /// </summary>
        public bool TryResetToInitialConditions(in FlightDynamicsInitialConditions initialConditions)
        {
            if (!IsInitialized) return Fail("Cannot reset flight dynamics before backend initialization.");
            if (!InitialConditionsAreFinite(initialConditions))
                return Fail("Recovery initial conditions contain a non-finite value.");
            return ResetToInitialConditions(initialConditions, false);
        }

        private bool ResetToInitialConditions(
            in FlightDynamicsInitialConditions initialConditions,
            bool allowConfiguredNativeFallback)
        {
            if (!_backend.Reset(initialConditions))
            {
                if (allowConfiguredNativeFallback &&
                    _backend.Kind == FlightDynamicsBackendKind.JSBSimNativeDevelopment &&
                    TryRuntimeFallback(_backend.LastError)) return true;
                return Fail(_backend.LastError);
            }
            LastResetInitialConditions = initialConditions;
            ApplyControls(NeutralIdleControls);
            _currentState = _backend.CurrentState;
            _previousState = _currentState;
            _accumulator?.Reset();
            ApplyBackendPose(_currentState);
            ApplyLegacyState(_currentState);
            return true;
        }

        private static bool InitialConditionsAreFinite(in FlightDynamicsInitialConditions value)
        {
            return IsFinite(value.latitudeDegrees) &&
                   IsFinite(value.longitudeDegrees) &&
                   IsFinite(value.altitudeMslMeters) &&
                   IsFinite(value.terrainElevationMslMeters) &&
                   IsFinite(value.calibratedAirspeedKnots) &&
                   IsFinite(value.headingDegrees) &&
                   IsFinite(value.pitchDegrees) &&
                   IsFinite(value.bankDegrees) &&
                   IsFinite(value.flightPathAngleDegrees) &&
                   value.calibratedAirspeedKnots >= 0d &&
                   value.altitudeMslMeters >= value.terrainElevationMslMeters;
        }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private bool TryInitializeBackend(
            IFlightDynamicsBackend candidate,
            FlightDynamicsBackendContext context,
            FlightDynamicsInitialConditions initialConditions,
            out string error)
        {
            if (!candidate.Initialize(context))
            {
                error = candidate.LastError;
                return false;
            }
            candidate.SetAtmosphere(FlightDynamicsAtmosphere.Calm);
            if (!candidate.Reset(initialConditions))
            {
                error = candidate.LastError;
                return false;
            }
            candidate.SetControls(NeutralIdleControls);
            _backend = candidate;
            LastResetInitialConditions = initialConditions;
            LastAppliedControls = FlightDynamicsControlSnapshot.From(NeutralIdleControls);
            _currentState = candidate.CurrentState;
            _previousState = _currentState;
            ApplyBackendPose(_currentState);
            ApplyLegacyState(_currentState);
            error = string.Empty;
            return true;
        }

        private void CompleteInitialization(double fixedStep)
        {
            LastError = string.Empty;
            Debug.Log($"[QuestFlight][FlightAuthority] {_backend.DisplayName}; " +
                      $"fixed step={fixedStep * 1000.0:0.###} ms; " +
                      $"release request={requestedBackend}.");
        }

        private FlightDynamicsBackendContext CreateContext(double fixedStep)
        {
            return new FlightDynamicsBackendContext
            {
                simulationRoot = simulationRoot,
                aircraftState = aircraftState,
                unityPrototype = unityPrototype,
                localOrigin = GeodeticReference.Kbdu,
                fixedDeltaTimeSeconds = fixedStep
            };
        }

        private void ApplyControls(AircraftControlState state)
        {
            LastAppliedControls = FlightDynamicsControlSnapshot.From(state);
            _backend.SetControls(state);
        }

        private bool StepBackend(double fixedDeltaTimeSeconds)
        {
            if (_backend == null || !_backend.IsInitialized) return false;
            _previousState = _currentState;
            if (!_backend.Advance(fixedDeltaTimeSeconds))
            {
                if (_backend.Kind == FlightDynamicsBackendKind.JSBSimNativeDevelopment &&
                    TryRuntimeFallback(_backend.LastError)) return true;
                return Fail(_backend.LastError);
            }
            _currentState = _backend.CurrentState;
            ApplyBackendPose(_currentState);
            ApplyLegacyState(_currentState);
            return true;
        }

        private bool TryRuntimeFallback(string nativeError)
        {
            FlightDynamicsState nativeState = _currentState;
            AircraftControlState activeControls = controls != null ? controls.Current : NeutralIdleControls;
            _backend?.Dispose();
            _backend = null;
            RestorePrototypeEnabledState();

            IFlightDynamicsBackend prototype = new UnityPrototypeFlightBackend();
            double fixedStep = _accumulator?.StepSeconds ?? 1.0 / Mathf.Max(30f, simulationRateHz);
            if (!prototype.Initialize(CreateContext(fixedStep)))
            {
                string fallbackError = prototype.LastError;
                prototype.Dispose();
                return Fail("Native JSBSim failed ('" + nativeError +
                            "') and Unity fallback initialization failed ('" + fallbackError + "').");
            }
            prototype.SetAtmosphere(FlightDynamicsAtmosphere.Calm);
            FlightDynamicsInitialConditions continuation = InitialConditionsFromState(nativeState);
            if (!prototype.Reset(continuation))
            {
                string fallbackError = prototype.LastError;
                prototype.Dispose();
                return Fail("Native JSBSim failed ('" + nativeError +
                            "') and Unity fallback reset failed ('" + fallbackError + "').");
            }
            prototype.SetControls(activeControls);
            _backend = prototype;
            LastAppliedControls = FlightDynamicsControlSnapshot.From(activeControls);
            LastResetInitialConditions = continuation;
            _currentState = prototype.CurrentState;
            _previousState = _currentState;
            ApplyBackendPose(_currentState);
            ApplyLegacyState(_currentState);
            FallbackReason = "Native JSBSim runtime failure: " + nativeError;
            LastError = string.Empty;
            Debug.LogWarning("[QuestFlight][FlightAuthority] " + FallbackReason +
                             " Continued with Unity prototype under the existing authority lease.", this);
            return true;
        }

        private static FlightDynamicsInitialConditions InitialConditionsFromState(FlightDynamicsState state)
        {
            double verticalFeetPerSecond = state.verticalSpeedFeetPerMinute / 60.0;
            double horizontalFeetPerSecond = state.groundSpeedKnots * 1.6878098571;
            return new FlightDynamicsInitialConditions
            {
                latitudeDegrees = state.latitudeDegrees,
                longitudeDegrees = state.longitudeDegrees,
                altitudeMslMeters = state.altitudeMslMeters,
                terrainElevationMslMeters = state.altitudeMslMeters - Math.Max(0.0, state.altitudeAglMeters),
                calibratedAirspeedKnots = Math.Max(0.0, state.calibratedAirspeedKnots),
                headingDegrees = state.headingDegrees,
                pitchDegrees = state.pitchDegrees,
                bankDegrees = state.bankDegrees,
                flightPathAngleDegrees = Math.Atan2(verticalFeetPerSecond,
                    Math.Max(0.01, horizontalFeetPerSecond)) * FlightFrameConversions.RadiansToDegrees,
                engineRunning = state.engineRpm > 100.0
            };
        }

        private void StepBackendUnchecked(double fixedDeltaTimeSeconds)
        {
            if (!IsInitialized) return;
            if (!StepBackend(fixedDeltaTimeSeconds)) enabled = false;
        }

        private void ApplyBackendPose(FlightDynamicsState state)
        {
            if (simulationRoot == null) return;
            simulationRoot.SetPositionAndRotation(state.positionUnityMeters, state.rotationUnity);
        }

        private void ApplyLegacyState(FlightDynamicsState state)
        {
            if (aircraftState == null) return;
            aircraftState.velocityWorld = state.velocityUnityMetersPerSecond;
            aircraftState.angularVelocityDeg = state.angularVelocityBodyDegreesPerSecond;
            aircraftState.airspeedKts = (float)state.calibratedAirspeedKnots;
            aircraftState.altitudeFt = (float)(state.altitudeAglMeters * FlightFrameConversions.MetersToFeet);
            aircraftState.verticalSpeedFpm = (float)state.verticalSpeedFeetPerMinute;
            aircraftState.headingDeg = (float)state.headingDegrees;
            aircraftState.pitchDeg = (float)state.pitchDegrees;
            aircraftState.bankDeg = (float)state.bankDegrees;
            aircraftState.angleOfAttackDeg = (float)state.angleOfAttackDegrees;
            aircraftState.slipSkid = (float)state.sideslipDegrees;
            aircraftState.engineRpm = (float)state.engineRpm;
            aircraftState.loadFactorG = (float)state.loadFactorG;
            aircraftState.onGround = state.weightOnWheels;
        }

        public static bool ShouldCoordinatorInterpolate(Transform candidateSimulationRoot, Transform candidatePresentationRoot)
        {
            if (candidateSimulationRoot == null || candidatePresentationRoot == null ||
                candidatePresentationRoot == candidateSimulationRoot) return false;
            AircraftReferenceFrameRig rig = candidateSimulationRoot.GetComponent<AircraftReferenceFrameRig>();
            return rig == null || !rig.enabled;
        }

        private void EnforceSinglePresentationOwner()
        {
            if (simulationRoot == null) return;
            AircraftReferenceFrameRig rig = simulationRoot.GetComponent<AircraftReferenceFrameRig>();
            if (rig != null && rig.enabled) presentationRoot = simulationRoot;
        }

        private void CapturePrototypeEnabledState()
        {
            if (_prototypeEnabledStateCaptured || unityPrototype == null) return;
            _authoredPrototypeEnabled = unityPrototype.enabled;
            _prototypeEnabledStateCaptured = true;
        }

        private void RestorePrototypeEnabledState()
        {
            if (_prototypeEnabledStateCaptured && unityPrototype != null)
                unityPrototype.enabled = _authoredPrototypeEnabled;
        }

        private bool ReleaseAndFail(string message)
        {
            _backend?.Dispose();
            _backend = null;
            RestorePrototypeEnabledState();
            _authorityLease?.Dispose();
            _authorityLease = null;
            return Fail(message);
        }

        private bool Fail(string message)
        {
            LastError = string.IsNullOrWhiteSpace(message) ? "Flight dynamics backend failed." : message;
            Debug.LogError("[QuestFlight][FlightAuthority] " + LastError, this);
            return false;
        }

        private void OnDestroy()
        {
            ShutdownBackend();
        }

        private void ShutdownBackend()
        {
            _backend?.Dispose();
            _backend = null;
            RestorePrototypeEnabledState();
            LastAppliedControls = FlightDynamicsControlSnapshot.NeutralIdle;
            _authorityLease?.Dispose();
            _authorityLease = null;
        }
    }
}
