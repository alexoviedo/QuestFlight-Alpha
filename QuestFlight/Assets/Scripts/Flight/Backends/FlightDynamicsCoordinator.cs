using System;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlightLab.Flight.Backends
{
    /// <summary>
    /// Product fixed-step coordinator. The clean repository intentionally compiles one backend,
    /// UnityPrototypeFlightBackend, so selection and fallback cannot create competing authorities.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlightDynamicsCoordinator : MonoBehaviour
    {
        [Header("Single product authority")]
        public FlightDynamicsBackendKind requestedBackend = FlightDynamicsBackendKind.UnityPrototype;
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

        public FlightDynamicsBackendKind ActiveBackendKind => FlightDynamicsBackendKind.UnityPrototype;
        public bool IsInitialized => _backend != null && _backend.IsInitialized;
        public bool CoordinatorOwnsPresentationInterpolation => ShouldCoordinatorInterpolate(simulationRoot, presentationRoot);
        public string LastError { get; private set; } = string.Empty;
        public string FallbackReason => string.Empty;
        public FlightDynamicsState CurrentState => _currentState;
        public FlightDynamicsControlSnapshot LastAppliedControls { get; private set; } = FlightDynamicsControlSnapshot.NeutralIdle;
        public FlightDynamicsInitialConditions LastResetInitialConditions { get; private set; }
        public double DroppedSimulationSeconds => _accumulator?.DroppedSeconds ?? 0.0;

        private UnityPrototypeFlightBackend _backend;
        private FlightDynamicsAuthorityLease _authorityLease;
        private FixedStepAccumulator _accumulator;
        private FlightDynamicsState _previousState;
        private FlightDynamicsState _currentState;
        private FlightDynamicsInitialConditions _configuredInitialConditions;
        private bool _hasConfiguredInitialConditions;
        private Action<double> _stepBackendAction;
        private static readonly AircraftControlState NeutralIdleControls = AircraftControlState.Neutral(0.10f);

        private void Awake()
        {
            if (!HasAuthoredReferences(out string error))
            {
                LastError = error;
                Debug.LogError("[QuestFlight][FlightAuthority] " + error, this);
                enabled = false;
                return;
            }
            _stepBackendAction = StepBackendUnchecked;
            EnforceSinglePresentationOwner();
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
                error = "The product coordinator must request UnityPrototype.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private void Start() => InitializeSelectedBackend();

        public bool InitializeSelectedBackend()
        {
            ShutdownBackend();
            requestedBackend = FlightDynamicsBackendKind.UnityPrototype;
            RefreshConfiguredInitialConditions();
            if (!FlightDynamicsAuthority.TryAcquire(simulationRoot, this, out _authorityLease, out string authorityError))
                return Fail(authorityError);

            double fixedStep = 1.0 / Mathf.Max(30f, simulationRateHz);
            _accumulator = new FixedStepAccumulator(fixedStep, maxStepsPerFixedUpdate);
            _backend = new UnityPrototypeFlightBackend();
            FlightDynamicsBackendContext context = new FlightDynamicsBackendContext
            {
                simulationRoot = simulationRoot,
                aircraftState = aircraftState,
                unityPrototype = unityPrototype,
                localOrigin = GeodeticReference.Kbdu,
                fixedDeltaTimeSeconds = fixedStep
            };
            if (!_backend.Initialize(context)) return ReleaseAndFail(_backend.LastError);
            _backend.SetAtmosphere(FlightDynamicsAtmosphere.Calm);
            if (!_backend.Reset(_configuredInitialConditions)) return ReleaseAndFail(_backend.LastError);

            LastResetInitialConditions = _configuredInitialConditions;
            ApplyControls(NeutralIdleControls);
            _currentState = _backend.CurrentState;
            _previousState = _currentState;
            ApplyLegacyState(_currentState);
            LastError = string.Empty;
            Debug.Log($"[QuestFlight][FlightAuthority] Unity prototype; fixed step={fixedStep * 1000.0:0.###} ms.");
            return true;
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
            if (!_backend.Reset(_configuredInitialConditions)) return Fail(_backend.LastError);
            LastResetInitialConditions = _configuredInitialConditions;
            ApplyControls(NeutralIdleControls);
            _currentState = _backend.CurrentState;
            _previousState = _currentState;
            _accumulator?.Reset();
            ApplyLegacyState(_currentState);
            return true;
        }

        private void ApplyControls(AircraftControlState state)
        {
            LastAppliedControls = FlightDynamicsControlSnapshot.From(state);
            _backend.SetControls(state);
        }

        private bool StepBackend(double fixedDeltaTimeSeconds)
        {
            _previousState = _currentState;
            if (!_backend.Advance(fixedDeltaTimeSeconds)) return Fail(_backend.LastError);
            _currentState = _backend.CurrentState;
            ApplyLegacyState(_currentState);
            return true;
        }

        private void StepBackendUnchecked(double fixedDeltaTimeSeconds) => StepBackend(fixedDeltaTimeSeconds);

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

        private bool ReleaseAndFail(string message)
        {
            _backend?.Dispose();
            _backend = null;
            _authorityLease?.Dispose();
            _authorityLease = null;
            return Fail(message);
        }

        private bool Fail(string message)
        {
            LastError = string.IsNullOrWhiteSpace(message) ? "Unity prototype backend failed." : message;
            Debug.LogError("[QuestFlight][FlightAuthority] " + LastError, this);
            return false;
        }

        private void OnDestroy() => ShutdownBackend();

        private void ShutdownBackend()
        {
            _backend?.Dispose();
            _backend = null;
            LastAppliedControls = FlightDynamicsControlSnapshot.NeutralIdle;
            _authorityLease?.Dispose();
            _authorityLease = null;
        }
    }
}
